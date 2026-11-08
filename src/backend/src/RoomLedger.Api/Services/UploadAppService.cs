using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Xin presigned URL để tải ảnh thẳng lên S3 (MD-04-FE-02, BR-16; MD-06-FE-01 dùng lại).
 * Tạo bản ghi photo PENDING_UPLOAD ngay lúc xin URL; photo-ingest chuyển sang RECEIVED khi file tới S3.
 *
 * @author Sang
 * @version 1.0
 */
public class UploadAppService(AppDbContext db, ICurrentUser user, IStorage storage)
{
    /**
     * Kiểm dữ liệu, file, quyền và trạng thái biên bản rồi trả presigned PUT bắt buộc x-amz-checksum-sha256.
     * Biên bản không thuộc người gọi → 404 (global query filter); đã khóa → 409 RECORD_LOCKED.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<UploadResponse> CreateAsync(CreateUploadRequest req, CancellationToken ct)
    {
        new Validator()
            .Require(req.TargetType == UploadTargetType.Inspection, "targetType", "Hiện chỉ hỗ trợ ảnh biên bản (INSPECTION)")
            .Require(req.TargetId is { } t && t != Guid.Empty, "targetId", "Thiếu mã biên bản")
            .Require(req.RoomAreaId is { } a && a != Guid.Empty, "roomAreaId", "Thiếu khu vực")
            .Text(req.FileName, "fileName", 1, 255, "Tên file")
            .Require(req.CaptureSource is not null, "captureSource", "Thiếu nguồn ảnh (CAMERA hoặc GALLERY)")
            .ThrowIfInvalid();
        var contentType = (req.ContentType ?? "").Trim().ToLowerInvariant();
        var ext = UploadRules.ValidatePhoto(contentType, req.Size ?? 0, req.Sha256 ?? "");

        var inspection = await db.Inspections.Include(i => i.Tenancy)
            .SingleOrDefaultAsync(i => i.Id == req.TargetId, ct) ?? throw new NotFoundException();
        if (inspection.Status == InspectionStatus.AwaitingConfirm)
        {
            throw new InvalidStateException("Biên bản đang chờ xác nhận; xóa một ảnh để mở lại rồi mới tải thêm");
        }
        if (inspection.Status != InspectionStatus.Open)
        {
            throw new RecordLockedException("Biên bản đã khóa, không tải thêm ảnh được");
        }

        var tenancy = inspection.Tenancy!;
        var areaOfRoom = await db.RoomAreas.AnyAsync(a => a.Id == req.RoomAreaId && a.RoomId == tenancy.RoomId, ct);
        if (!areaOfRoom)
        {
            throw new ValidationException([new FieldError("roomAreaId", "Khu vực không thuộc phòng của biên bản")]);
        }

        var inArea = await db.Photos.CountAsync(p => p.InspectionId == inspection.Id && p.RoomAreaId == req.RoomAreaId, ct);
        if (inArea >= UploadRules.MaxPhotosPerArea)
        {
            throw new BadRequestException(ErrorCodes.InvalidFile, $"Mỗi khu vực tối đa {UploadRules.MaxPhotosPerArea} ảnh");
        }

        var photo = new Photo
        {
            InspectionId = inspection.Id,
            RoomAreaId = req.RoomAreaId!.Value,
            UploadedBy = user.UserId,
            UploaderRole = user.Role ?? throw new ForbiddenException(),
            FileName = req.FileName!.Trim(),
            ContentType = contentType,
            Size = req.Size!.Value,
            Sha256 = req.Sha256!,
            CaptureSource = req.CaptureSource!.Value,
        };
        photo.IncomingKey = UploadRules.BuildIncomingKey(tenancy.LandlordId, tenancy.Id, inspection.Id, photo.Id, ext);
        db.Photos.Add(photo);
        db.Audit(AuditEventTypes.UploadRequested, nameof(Photo), photo.Id);
        await db.SaveChangesAsync(ct);

        var upload = storage.CreateUploadUrl(photo.IncomingKey, contentType, photo.Sha256, UploadRules.UrlLifetime);
        return new UploadResponse(photo.Id, upload.Url, upload.Method, upload.Headers, upload.ExpiresAt);
    }
}
