using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Ai.Imaging;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.PhotoDerive;

/**
 * Sự kiện PhotoReceived (EventBridge): tạo bản xem JPEG và thumbnail 400 px trong bucket derived, xóa GPS,
 * RECEIVED → READY (BR-16 RULE 5). Chạy trong VPC vì phải cập nhật bảng photo (RDS chỉ vào được từ VPC);
 * S3 đi qua Gateway endpoint nên không cần internet.
 *
 * @author Sang
 * @version 1.0
 */
public class Function
{
    private readonly Func<AppDbContext> _db;
    private readonly IObjectStore _store;

    public Function() : this(FunctionDb.Create, FunctionAws.ObjectStore)
    {
    }

    public Function(Func<AppDbContext> db, IObjectStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task HandleAsync(CloudWatchEvent<PhotoEventDetail> input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        await using var db = _db();
        var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == input.Detail.PhotoId, ct);
        if (photo is null || photo.Status != FileStatus.Received)
        {
            context.Logger.LogInformation($"Bỏ qua ảnh {input.Detail.PhotoId}: {photo?.Status.ToString() ?? "không tồn tại"}");
            return;
        }

        DerivedImages derived;
        await using (var original = await _store.OpenReadAsync(StorageBucket.Incoming, photo.IncomingKey, ct))
        {
            derived = PhotoImaging.CreateDerived(original);
        }

        var viewKey = PhotoRules.ViewKey(photo.IncomingKey);
        await _store.PutAsync(StorageBucket.Derived, viewKey, derived.ViewJpeg, "image/jpeg", ct);
        await _store.PutAsync(StorageBucket.Derived, PhotoRules.ThumbnailKey(photo.IncomingKey), derived.ThumbnailJpeg, "image/jpeg", ct);

        photo.DerivedKey = viewKey;
        photo.Status = FileStatus.Ready;
        await db.SaveChangesAsync(ct);
        context.Logger.LogInformation($"Ảnh {photo.Id} READY");
    }
}
