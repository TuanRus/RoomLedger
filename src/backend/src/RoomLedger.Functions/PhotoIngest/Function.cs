using System.Net;
using Amazon.Lambda.Core;
using Amazon.Lambda.CloudWatchEvents;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Ai.Imaging;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.PhotoIngest;

/**
 * S3 "Object Created" của bucket incoming qua EventBridge (trong VPC): ghi nhận ảnh đã tới S3 (BR-16 RULE 4).
 * S3 đã kiểm x-amz-checksum-sha256 lúc nhận PUT (header nằm trong chữ ký presigned URL), nên ở đây không băm lại.
 * Đọc EXIF DateTimeOriginal, cảnh báo ảnh cũ hơn PHOTO_MAX_AGE_DAYS, PENDING_UPLOAD → RECEIVED, phát PhotoReceived.
 * Chạy lại an toàn: S3 có thể gửi trùng sự kiện; ảnh còn RECEIVED thì phát lại sự kiện (lần trước phát lỗi).
 *
 * @author Sang
 * @version 1.0
 */
public class Function
{
    private readonly Func<AppDbContext> _db;
    private readonly IObjectStore _store;
    private readonly IEventPublisher _events;
    private readonly TimeProvider _clock;
    private readonly int _maxAgeDays;

    public Function() : this(FunctionDb.Create, FunctionAws.ObjectStore, FunctionAws.Events, TimeProvider.System, FunctionAws.PhotoMaxAgeDays)
    {
    }

    public Function(Func<AppDbContext> db, IObjectStore store, IEventPublisher events, TimeProvider clock, int maxAgeDays)
    {
        _db = db;
        _store = store;
        _events = events;
        _clock = clock;
        _maxAgeDays = maxAgeDays;
    }

    /**
     * Nhận "Object Created" của bucket incoming qua EventBridge (rule trong WorkflowStack). Key của RoomLedger chỉ gồm
     * GUID, '/' và '.', nên giải mã URL (nếu S3 có mã hóa) không làm đổi key.
     *
     * @author Sang
     * @version 1.0
     */
    public Task HandleAsync(CloudWatchEvent<S3ObjectCreatedDetail> input, ILambdaContext context) =>
        IngestAsync(WebUtility.UrlDecode(input.Detail.Object.Key), context, CancellationToken.None);

    private async Task IngestAsync(string key, ILambdaContext context, CancellationToken ct)
    {
        var photoId = PhotoRules.PhotoIdFromKey(key);
        await using var db = _db();
        var photo = photoId is null ? null
            : await db.Photos.SingleOrDefaultAsync(p => p.Id == photoId && p.IncomingKey == key, ct);
        if (photo is null)
        {
            context.Logger.LogWarning($"Bỏ qua object không có bản ghi photo: {key}");
            return;
        }
        if (photo.Status is FileStatus.Ready or FileStatus.Sealed)
        {
            context.Logger.LogInformation($"Ảnh {photo.Id} đã xử lý ({photo.Status}), bỏ qua sự kiện trùng");
            return;
        }

        if (photo.Status == FileStatus.PendingUpload)
        {
            await using (var original = await _store.OpenReadAsync(StorageBucket.Incoming, key, ct))
            {
                // timestamptz chỉ nhận offset 0: lưu UTC (cùng thời điểm, FE hiển thị theo giờ địa phương)
                photo.ExifTakenAt = PhotoImaging.ReadTakenAt(original)?.ToUniversalTime();
            }
            photo.OldPhotoWarning = PhotoRules.IsOldPhoto(photo.ExifTakenAt, _clock.GetUtcNow(), _maxAgeDays);
            photo.Status = FileStatus.Received;
            await db.SaveChangesAsync(ct);
        }

        await _events.PublishAsync(PhotoEventDetail.PhotoReceived, new PhotoEventDetail(photo.Id), ct);
        context.Logger.LogInformation($"Ảnh {photo.Id} RECEIVED, oldPhotoWarning={photo.OldPhotoWarning}");
    }
}
