using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Thông tin chuyển khoản theo nhà trọ: số tài khoản, mẫu nội dung, mã QR do chủ nhà tải lên.
 * QR đi: POST /uploads (PROPERTY_QR) → PUT lên incoming → :confirm (kiểm object có thật và ≤ 2 MB, chép sang derived vì
 * incoming tự xóa sau 7 ngày) → qrUrl. QR cũ vẫn dùng được cho tới khi bản mới được xác nhận.
 *
 * @author Tuấn
 * @version 1.0
 */
public class PaymentInfoAppService(AppDbContext db, PropertyAppService properties, IStorage storage, IObjectStore objects)
{
    /** Dài hơn link ảnh đồng hồ: chi tiết hóa đơn được FE giữ trong bộ nhớ nên link QR phải còn dùng được lâu. */
    public static readonly TimeSpan QrLifetime = TimeSpan.FromHours(1);

    private static readonly Dictionary<string, string> QrTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = "png",
        ["image/jpeg"] = "jpg",
        ["image/webp"] = "webp",
    };

    public static readonly FileRules QrRules = new(QrTypes, PaymentRules.MaxQrBytes);

    /**
     * Xem thông tin chuyển khoản của nhà trọ (chỉ chủ nhà; nhà người khác trả 404).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PaymentInfoResponse> GetAsync(Guid propertyId, CancellationToken ct) =>
        ToResponse(await properties.GetOwnedAsync(propertyId, ct));

    /**
     * Lưu ngân hàng, số tài khoản (chỉ giữ chữ số), chủ tài khoản và mẫu nội dung chuyển khoản.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PaymentInfoResponse> UpdateAsync(Guid propertyId, UpdatePaymentInfoRequest req, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        PaymentRules.ValidatePaymentInfo(req.BankName, req.AccountNumber, req.AccountHolder, req.TransferNoteTemplate);
        property.BankName = req.BankName!.Trim();
        property.AccountNumber = PaymentRules.NormalizeAccountNumber(req.AccountNumber!);
        property.AccountHolder = req.AccountHolder!.Trim();
        property.TransferNoteTemplate = PaymentRules.NormalizeTemplate(req.TransferNoteTemplate);
        db.Audit(AuditEventTypes.PaymentInfoUpdated, nameof(Property), property.Id);
        await db.SaveChangesAsync(ct);
        return ToResponse(property);
    }

    /**
     * POST /uploads với PROPERTY_QR: kiểm file (png/jpeg/webp ≤ 2 MB), lưu key chờ trong property, trả presigned PUT.
     * Chưa đụng tới QR đang dùng; file chờ cũ chưa xác nhận bị xóa (best-effort).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<UploadUrlResponse> CreateQrUploadAsync(CreateUploadRequest req, CancellationToken ct)
    {
        var ext = UploadAppService.CheckFile(req, QrRules);
        var property = await properties.GetOwnedAsync(req.TargetId!.Value, ct);
        var fileId = UuidV7.NewGuid();
        var key = UploadAppService.Key(property.LandlordId, property.Id, property.Id, fileId, ext);
        var previous = property.QrPendingKey;
        property.QrPendingKey = key;
        db.Audit(AuditEventTypes.UploadRequested, nameof(Property), property.Id);
        await db.SaveChangesAsync(ct);
        if (previous is not null)
        {
            await TryDeleteAsync(StorageBucket.Incoming, previous, ct);
        }

        var url = storage.CreateUploadUrl(key, req.ContentType!.ToLowerInvariant(), req.Sha256!, UploadAppService.UploadLifetime);
        return new UploadUrlResponse(fileId, url.Url, url.Method, url.Headers, url.ExpiresAt);
    }

    /**
     * Xác nhận đã tải QR lên: object phải có thật trong incoming và ≤ 2 MB. Chép sang derived (bền), xóa file incoming
     * và QR cũ, rồi QR chuyển RECEIVED. Chưa có object → 409 QR_NOT_UPLOADED (FE tải lại rồi xác nhận lại).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PaymentInfoResponse> ConfirmQrAsync(Guid propertyId, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        if (property.QrPendingKey is null && property.QrStatus is FileStatus.Received or FileStatus.Ready)
        {
            return ToResponse(property);   // gọi lại sau khi đã xác nhận (mạng chậm, bấm hai lần): không lỗi
        }

        var pending = property.QrPendingKey
            ?? throw new InvalidStateException("Chưa có yêu cầu tải mã QR, hãy xin link tải lên trước");
        var size = await objects.GetSizeAsync(StorageBucket.Incoming, pending, ct)
            ?? throw new ConflictException(ErrorCodes.QrNotUploaded, "Chưa thấy file mã QR trên hệ thống, hãy tải lên lại");
        if (size > PaymentRules.MaxQrBytes)
        {
            await TryDeleteAsync(StorageBucket.Incoming, pending, ct);
            property.QrPendingKey = null;
            await db.SaveChangesAsync(ct);
            throw new BadRequestException(ErrorCodes.InvalidFile, $"File phải nhỏ hơn {PaymentRules.MaxQrBytes / 1024 / 1024} MB");
        }

        var previousKey = property.QrKey;
        var liveKey = $"qr/{property.LandlordId}/{property.Id}/{Path.GetFileName(pending)}";
        try
        {
            await objects.CopyAsync(StorageBucket.Incoming, pending, StorageBucket.Derived, liveKey, ct);
        }
        catch (FileNotFoundException)
        {
            // Object biến mất giữa lúc kiểm và lúc chép (lifecycle, xóa tay): giống như chưa tải
            throw new ConflictException(ErrorCodes.QrNotUploaded, "Chưa thấy file mã QR trên hệ thống, hãy tải lên lại");
        }

        property.QrKey = liveKey;
        property.QrStatus = FileStatus.Received;
        property.QrPendingKey = null;
        db.Audit(AuditEventTypes.PaymentQrConfirmed, nameof(Property), property.Id);
        try
        {
            // Version (xmin) là concurrency token: nếu lúc này chủ nhà xin link tải mới (đổi QrPendingKey), lưu sẽ lỗi
            // và yêu cầu mới không bị xóa nhầm
            await db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            await TryDeleteAsync(StorageBucket.Derived, liveKey, ct);
            throw;
        }

        await TryDeleteAsync(StorageBucket.Incoming, pending, ct);
        if (previousKey is not null)
        {
            await TryDeleteAsync(StorageBucket.Derived, previousKey, ct);
        }

        return ToResponse(property);
    }

    /**
     * Gỡ QR: xóa các trường QR rồi xóa object trên S3 (best-effort, lỗi S3 không làm hỏng thao tác).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PaymentInfoResponse> RemoveQrAsync(Guid propertyId, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        var live = property.QrKey;
        var pending = property.QrPendingKey;
        property.QrKey = null;
        property.QrStatus = null;
        property.QrPendingKey = null;
        db.Audit(AuditEventTypes.PaymentQrRemoved, nameof(Property), property.Id);
        await db.SaveChangesAsync(ct);
        if (live is not null)
        {
            await TryDeleteAsync(StorageBucket.Derived, live, ct);
        }

        if (pending is not null)
        {
            await TryDeleteAsync(StorageBucket.Incoming, pending, ct);
        }

        return ToResponse(property);
    }

    /**
     * Xóa toàn bộ thông tin chuyển khoản (ngân hàng, số tài khoản, chủ tài khoản, mẫu nội dung) và cả mã QR;
     * object trên S3 xóa best-effort. Hóa đơn sau đó có payment = null.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PaymentInfoResponse> RemoveAllAsync(Guid propertyId, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        var live = property.QrKey;
        var pending = property.QrPendingKey;
        property.BankName = null;
        property.AccountNumber = null;
        property.AccountHolder = null;
        property.TransferNoteTemplate = null;
        property.QrKey = null;
        property.QrStatus = null;
        property.QrPendingKey = null;
        db.Audit(AuditEventTypes.PaymentInfoRemoved, nameof(Property), property.Id);
        await db.SaveChangesAsync(ct);
        if (live is not null)
        {
            await TryDeleteAsync(StorageBucket.Derived, live, ct);
        }

        if (pending is not null)
        {
            await TryDeleteAsync(StorageBucket.Incoming, pending, ct);
        }

        return ToResponse(property);
    }

    /**
     * Khối thanh toán trên hóa đơn: null nếu nhà trọ chưa nhập đủ ngân hàng, số tài khoản, chủ tài khoản.
     *
     * @author Tuấn
     * @version 1.0
     */
    public PaymentResponse? ForInvoice(Property property, string roomCode, string period)
    {
        if (string.IsNullOrWhiteSpace(property.BankName) || string.IsNullOrWhiteSpace(property.AccountNumber)
            || string.IsNullOrWhiteSpace(property.AccountHolder))
        {
            return null;
        }

        return new PaymentResponse(property.BankName, property.AccountNumber, property.AccountHolder,
            PaymentRules.FillTransferNote(property.TransferNoteTemplate, roomCode, period), QrUrl(property));
    }

    /** qrStatus: trạng thái QR đang dùng; chỉ khi chưa có QR nào mà đang chờ tải thì báo PENDING_UPLOAD. */
    private PaymentInfoResponse ToResponse(Property p) => new(
        p.BankName, p.AccountNumber, p.AccountHolder, p.TransferNoteTemplate ?? PaymentRules.DefaultTemplate, QrUrl(p),
        p.QrStatus ?? (p.QrPendingKey is not null ? FileStatus.PendingUpload : null));

    private string? QrUrl(Property p) =>
        p.QrKey is not null && p.QrStatus is FileStatus.Received or FileStatus.Ready
            ? storage.CreateDownloadUrl(StorageBucket.Derived, p.QrKey, QrLifetime)
            : null;

    private async Task TryDeleteAsync(StorageBucket bucket, string key, CancellationToken ct)
    {
        try
        {
            await objects.DeleteAsync(bucket, key, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Object mồ côi tự dọn bằng lifecycle (incoming) hoặc xóa tay; không để lỗi S3 chặn thao tác của chủ nhà
        }
    }
}
