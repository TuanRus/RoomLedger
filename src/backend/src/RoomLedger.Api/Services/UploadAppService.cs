using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.StateMachines;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Kiểm file trước khi xin URL: định dạng cho phép và dung lượng tối đa.
 *
 * @author Tuấn
 * @version 1.0
 */
public record FileRules(IReadOnlyDictionary<string, string> Extensions, long MaxBytes);

/**
 * POST /uploads: kiểm quyền, trạng thái và file rồi trả presigned PUT tới bucket incoming.
 * File đi thẳng từ trình duyệt lên S3, không qua Lambda. Bản ghi file được tạo ở trạng thái PENDING_UPLOAD;
 * photo-ingest (S3 ObjectCreated) đổi sang RECEIVED khi file lên xong.
 *
 * @author Tuấn
 * @version 1.0
 */
public class UploadAppService(AppDbContext db, ICurrentUser user, IStorage storage)
{
    public static readonly TimeSpan UploadLifetime = TimeSpan.FromMinutes(5);
    public const int MaxPhotosPerArea = 20;

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/heic"] = "heic",
        ["image/heif"] = "heif",
    };

    public static readonly FileRules PhotoRules = new(ImageTypes, 25L * 1024 * 1024);

    public static readonly FileRules ScanRules = new(
        new Dictionary<string, string>(ImageTypes, StringComparer.OrdinalIgnoreCase) { ["application/pdf"] = "pdf" },
        Core.Services.TenancyService.MaxScanBytes);

    /**
     * Tạo presigned URL theo targetType: INSPECTION (kèm areaId), TENANCY_VERSION, METER_READING.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<UploadUrlResponse> CreateAsync(CreateUploadRequest req, CancellationToken ct)
    {
        var v = new Validator()
            .Require(req.TargetType is not null, "targetType", "Thiếu targetType")
            .Require(req.TargetId is not null, "targetId", "Thiếu targetId")
            .Text(req.FileName, "fileName", 1, 255, "Tên file")
            .Require(IsSha256Base64(req.Sha256), "sha256", "sha256 phải là SHA-256 mã hóa base64");
        v.ThrowIfInvalid();

        return req.TargetType!.Value switch
        {
            UploadTargetType.Inspection => await ForInspectionAsync(req, ct),
            UploadTargetType.TenancyVersion => await ForTenancyVersionAsync(req, ct),
            _ => await ForMeterReadingAsync(req, ct),
        };
    }

    /**
     * Ảnh đồng hồ cho chỉ số (dùng chung với POST /rooms/{roomId}/meter-readings).
     * Đặt lại chỉ số về PENDING_AI để AI đọc ảnh mới.
     *
     * @author Tuấn
     * @version 1.0
     */
    public UploadUrlResponse AttachMeterPhoto(MeterReading reading, Guid landlordId, CreateUploadRequest req)
    {
        var ext = CheckFile(req, PhotoRules);
        var fileId = UuidV7.NewGuid();
        var key = Key(landlordId, reading.TenancyId, reading.Id, fileId, ext);
        reading.PhotoFileName = req.FileName!.Trim();
        reading.PhotoContentType = req.ContentType!.ToLowerInvariant();
        reading.PhotoSize = req.Size;
        reading.PhotoSha256 = req.Sha256;
        reading.PhotoStatus = FileStatus.PendingUpload;
        reading.PhotoKey = key;
        reading.Status = MeterReadingStatus.PendingAi;
        reading.AiValue = null;
        reading.AiConfidence = null;
        reading.AiRawJson = null;
        reading.ReviewReason = null;
        return Presign(fileId, key, req);
    }

    /**
     * Kiểm định dạng và dung lượng; trả phần mở rộng lấy theo contentType (không lấy từ tên file người dùng).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string CheckFile(CreateUploadRequest req, FileRules rules)
    {
        if (req.ContentType is null || !rules.Extensions.TryGetValue(req.ContentType, out var ext))
        {
            throw new BadRequestException(ErrorCodes.InvalidFile, "Định dạng file không được hỗ trợ");
        }

        if (req.Size is null or <= 0 || req.Size > rules.MaxBytes)
        {
            throw new BadRequestException(ErrorCodes.InvalidFile, $"File phải nhỏ hơn {rules.MaxBytes / 1024 / 1024} MB");
        }

        return ext;
    }

    /**
     * SHA-256 base64 hợp lệ: giải mã ra đúng 32 byte.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static bool IsSha256Base64(string? value)
    {
        if (value is not { Length: 44 })
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[33];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 32;
    }

    /**
     * Khóa S3: {landlordId}/{tenancyId}/{targetId}/{fileId}.{ext}. Không có tên người, số phòng hay số điện thoại.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string Key(Guid landlordId, Guid tenancyId, Guid targetId, Guid fileId, string ext) =>
        $"{landlordId}/{tenancyId}/{targetId}/{fileId}.{ext}";

    private async Task<UploadUrlResponse> ForInspectionAsync(CreateUploadRequest req, CancellationToken ct)
    {
        if (req.AreaId is null)
        {
            throw ValidationException.For("areaId", "Thiếu areaId");
        }

        var ext = CheckFile(req, PhotoRules);
        var inspection = await db.Inspections.Include(i => i.Tenancy)
            .FirstOrDefaultAsync(i => i.Id == req.TargetId, ct)
            ?? throw new NotFoundException("Không tìm thấy biên bản");
        if (inspection.Status is InspectionStatus.Locked or InspectionStatus.Settled)
        {
            throw new RecordLockedException("Biên bản đã khóa, không gửi thêm ảnh được");
        }

        if (inspection.Status != InspectionStatus.Open)
        {
            throw new InvalidStateException("Biên bản không còn nhận ảnh");
        }

        var tenancy = inspection.Tenancy!;
        var areaOk = await db.RoomAreas.AnyAsync(a => a.Id == req.AreaId && a.RoomId == tenancy.RoomId, ct);
        if (!areaOk)
        {
            throw new NotFoundException("Không tìm thấy khu vực");
        }

        var count = await db.Photos.CountAsync(p => p.InspectionId == inspection.Id && p.RoomAreaId == req.AreaId, ct);
        if (count >= MaxPhotosPerArea)
        {
            throw new BadRequestException(ErrorCodes.InvalidFile, $"Mỗi khu vực tối đa {MaxPhotosPerArea} ảnh");
        }

        var uploaderId = user.UserId;
        var photo = new Photo
        {
            InspectionId = inspection.Id,
            RoomAreaId = req.AreaId.Value,
            UploadedBy = uploaderId,
            UploaderRole = uploaderId == tenancy.LandlordId ? UserRole.Landlord : UserRole.Tenant,
            FileName = req.FileName!.Trim(),
            ContentType = req.ContentType!.ToLowerInvariant(),
            Size = req.Size!.Value,
            Sha256 = req.Sha256!,
        };
        photo.IncomingKey = Key(tenancy.LandlordId, tenancy.Id, inspection.Id, photo.Id, ext);
        db.Photos.Add(photo);
        await db.SaveChangesAsync(ct);
        return Presign(photo.Id, photo.IncomingKey, req);
    }

    private async Task<UploadUrlResponse> ForTenancyVersionAsync(CreateUploadRequest req, CancellationToken ct)
    {
        var ext = CheckFile(req, ScanRules);
        var version = await db.TenancyVersions.Include(v => v.Tenancy)
            .FirstOrDefaultAsync(v => v.Id == req.TargetId, ct)
            ?? throw new NotFoundException("Không tìm thấy phiên bản hợp đồng");
        var tenancy = version.Tenancy!;
        if (tenancy.LandlordId != user.UserId)
        {
            throw new ForbiddenException("Chỉ chủ nhà tải được bản scan hợp đồng");
        }

        TenancyStateMachine.EnsureCanUploadFiles(version);
        var count = await db.TenancyFiles.CountAsync(f => f.TenancyVersionId == version.Id, ct);
        if (count >= Core.Services.TenancyService.MaxFilesPerVersion)
        {
            throw new BadRequestException(ErrorCodes.InvalidFile, $"Mỗi phiên bản tối đa {Core.Services.TenancyService.MaxFilesPerVersion} file");
        }

        var file = new TenancyFile
        {
            TenancyVersionId = version.Id,
            FileName = req.FileName!.Trim(),
            ContentType = req.ContentType!.ToLowerInvariant(),
            Size = req.Size!.Value,
            Sha256 = req.Sha256!,
        };
        file.IncomingKey = Key(tenancy.LandlordId, tenancy.Id, version.Id, file.Id, ext);
        // File mới làm AI đọc lại điều khoản
        version.AiStatus = AiStatus.Pending;
        db.TenancyFiles.Add(file);
        db.Audit(AuditEventTypes.UploadRequested, nameof(TenancyFile), file.Id);
        await db.SaveChangesAsync(ct);
        return Presign(file.Id, file.IncomingKey, req);
    }

    private async Task<UploadUrlResponse> ForMeterReadingAsync(CreateUploadRequest req, CancellationToken ct)
    {
        var reading = await db.MeterReadings.Include(m => m.Tenancy)
            .FirstOrDefaultAsync(m => m.Id == req.TargetId, ct)
            ?? throw new NotFoundException("Không tìm thấy chỉ số");
        if (reading.Status == MeterReadingStatus.Confirmed)
        {
            throw new ConflictException(ErrorCodes.PeriodClosed, "Kỳ này đã có chỉ số được xác nhận");
        }

        if (reading.Tenancy!.Status is not (TenancyStatus.Active or TenancyStatus.MovingOut))
        {
            throw new InvalidStateException("Hợp đồng chưa có hiệu lực");
        }

        var response = AttachMeterPhoto(reading, reading.Tenancy.LandlordId, req);
        await db.SaveChangesAsync(ct);
        return response;
    }

    private UploadUrlResponse Presign(Guid fileId, string key, CreateUploadRequest req)
    {
        var url = storage.CreateUploadUrl(key, req.ContentType!.ToLowerInvariant(), req.Sha256!, UploadLifetime);
        return new UploadUrlResponse(fileId, url.Url, url.Method, url.Headers, url.ExpiresAt);
    }
}
