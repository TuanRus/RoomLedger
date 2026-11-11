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
 * Nghiệp vụ chỉ số điện nước phía API (BR-20, BR-21). AI đọc ảnh đồng hồ (read-document,
 * persist-results) do Sang làm; API tạo chỉ số, trả URL tải ảnh, cho chủ nhà sửa và xác nhận.
 *
 * @author Tuấn
 * @version 1.0
 */
public class MeterAppService(AppDbContext db, ICurrentUser user, IStorage storage, UploadAppService uploads, PropertyAppService properties)
{
    private static readonly TimeSpan DownloadLifetime = TimeSpan.FromMinutes(5);

    /**
     * Tạo (hoặc tải lại ảnh cho) chỉ số kỳ của phòng. Kỳ đã có chỉ số xác nhận thì PERIOD_CLOSED.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<(CreateMeterReadingResponse Response, bool Created)> CreateAsync(Guid roomId, CreateMeterReadingRequest req, CancellationToken ct)
    {
        var kind = req.Kind ?? throw ValidationException.For("kind", "Thiếu loại đồng hồ");
        var period = Period.Parse(req.Period);
        var uploadReq = new CreateUploadRequest(UploadTargetType.MeterReading, null, null, req.FileName, req.ContentType, req.Size, req.Sha256);
        new Validator()
            .Text(req.FileName, "fileName", 1, 255, "Tên file")
            .Require(UploadAppService.IsSha256Base64(req.Sha256), "sha256", "sha256 phải là SHA-256 mã hóa base64")
            .ThrowIfInvalid();

        var tenancy = await db.Tenancies.FirstOrDefaultAsync(t => t.RoomId == roomId
            && (t.Status == TenancyStatus.Active || t.Status == TenancyStatus.MovingOut), ct)
            ?? throw new NotFoundException("Phòng không có hợp đồng đang hiệu lực");

        var reading = await db.MeterReadings.FirstOrDefaultAsync(m => m.RoomId == roomId && m.Period == period && m.Kind == kind, ct);
        var created = reading is null;
        if (reading is { Status: MeterReadingStatus.Confirmed })
        {
            throw new ConflictException(ErrorCodes.PeriodClosed, "Kỳ này đã có chỉ số được xác nhận");
        }

        if (reading is null)
        {
            reading = new MeterReading { RoomId = roomId, TenancyId = tenancy.Id, Period = period, Kind = kind };
            db.MeterReadings.Add(reading);
        }

        var upload = uploads.AttachMeterPhoto(reading, tenancy.LandlordId, uploadReq);
        await db.SaveChangesAsync(ct);
        var previous = await PreviousValueAsync(reading.RoomId, reading.Kind, reading.Period, ct);
        return (new CreateMeterReadingResponse(ToResponse(reading, previous), upload), created);
    }

    /**
     * Bảng chỉ số của kỳ cho mọi phòng có hợp đồng hiệu lực trong nhà trọ.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<IReadOnlyList<RoomReadingsResponse>> ListAsync(Guid propertyId, string? period, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        var p = Period.Parse(period);
        var rooms = await db.Tenancies.AsNoTracking()
            .Where(t => t.Room!.PropertyId == property.Id && (t.Status == TenancyStatus.Active || t.Status == TenancyStatus.MovingOut))
            .OrderBy(t => t.Room!.Code)
            .Select(t => new { t.RoomId, t.Room!.Code, TenancyId = t.Id })
            .ToListAsync(ct);
        var roomIds = rooms.Select(r => r.RoomId).ToList();
        var readings = await db.MeterReadings.AsNoTracking()
            .Where(m => roomIds.Contains(m.RoomId) && m.Period == p)
            .ToListAsync(ct);
        var previous = await PreviousValuesAsync(roomIds, p, ct);

        MeterReadingResponse? Find(Guid roomId, MeterKind kind) =>
            readings.FirstOrDefault(m => m.RoomId == roomId && m.Kind == kind) is { } m
                ? ToResponse(m, previous.GetValueOrDefault((roomId, kind)))
                : null;

        return rooms.Select(r => new RoomReadingsResponse(r.RoomId, r.Code, r.TenancyId,
            Find(r.RoomId, MeterKind.Electricity), Find(r.RoomId, MeterKind.Water))).ToList();
    }

    /**
     * Chủ nhà sửa và/hoặc xác nhận một chỉ số (BR-21). Sửa số khác AI thì ghi EditedByLandlord.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<MeterReadingResponse> UpdateAsync(Guid readingId, UpdateMeterReadingRequest req, CancellationToken ct)
    {
        var reading = await GetOwnedAsync(readingId, ct);
        if (reading.Status == MeterReadingStatus.Confirmed)
        {
            throw new InvalidStateException("Chỉ số đã xác nhận; hãy sửa qua khiếu nại của hóa đơn");
        }

        if (req.Note is { Length: > 500 })
        {
            throw ValidationException.For("note", "Ghi chú tối đa 500 ký tự");
        }

        var previous = await PreviousValueAsync(reading.RoomId, reading.Kind, reading.Period, ct);
        if (req.MeterReplaced is not null)
        {
            reading.MeterReplaced = req.MeterReplaced.Value;
        }

        if (req.Note is not null)
        {
            reading.Note = req.Note.Trim();
        }

        if (req.Value is not null)
        {
            MeterReadingService.ValidateConfirmedValue(req.Value, previous, reading.MeterReplaced);
            reading.Value = req.Value;
            reading.EditedByLandlord = reading.AiValue != req.Value;
            if (reading.Status == MeterReadingStatus.PendingAi)
            {
                // Chủ nhà nhập tay khi AI chưa đọc xong hoặc không có ảnh
                reading.Status = MeterReadingStatus.PendingConfirm;
            }
        }

        if (req.Confirm == true)
        {
            MeterReadingService.EnsureCanConfirm(reading.Status);
            Confirm(reading, previous);
        }

        await db.SaveChangesAsync(ct);
        return ToResponse(reading, previous);
    }

    /**
     * Xác nhận mọi chỉ số PENDING_CONFIRM của kỳ (BR-21 RULE 2); chỉ số bị đánh dấu NEEDS_REVIEW giữ nguyên.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<ConfirmAllReadingsResponse> ConfirmAllAsync(Guid propertyId, ConfirmAllReadingsRequest req, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        var p = Period.Parse(req.Period);
        var readings = await db.MeterReadings
            .Where(m => m.Period == p && m.Tenancy!.Room!.PropertyId == property.Id && m.Tenancy.LandlordId == property.LandlordId
                && (m.Status == MeterReadingStatus.PendingConfirm || m.Status == MeterReadingStatus.NeedsReview))
            .ToListAsync(ct);
        var previous = await PreviousValuesAsync(readings.Select(r => r.RoomId).Distinct().ToList(), p, ct);

        var skipped = new List<Guid>();
        var confirmed = 0;
        foreach (var reading in readings)
        {
            var prev = previous.GetValueOrDefault((reading.RoomId, reading.Kind));
            var value = reading.Value ?? reading.AiValue;
            var valid = reading.Status == MeterReadingStatus.PendingConfirm && value is >= 0
                && (prev is null || reading.MeterReplaced || value >= prev);
            if (!valid)
            {
                skipped.Add(reading.Id);
                continue;
            }

            reading.Value = value;
            Confirm(reading, prev);
            confirmed++;
        }

        await db.SaveChangesAsync(ct);
        return new ConfirmAllReadingsResponse(confirmed, skipped);
    }

    /**
     * Chỉ số xác nhận gần nhất của kỳ trước (cùng phòng, cùng loại đồng hồ).
     *
     * @author Tuấn
     * @version 1.0
     */
    public Task<long?> PreviousValueAsync(Guid roomId, MeterKind kind, string period, CancellationToken ct) =>
        db.MeterReadings.AsNoTracking()
            .Where(m => m.RoomId == roomId && m.Kind == kind && m.Status == MeterReadingStatus.Confirmed
                && string.Compare(m.Period, period) < 0)
            .OrderByDescending(m => m.Period)
            .Select(m => m.Value)
            .FirstOrDefaultAsync(ct);

    /**
     * Chỉ số kỳ trước của nhiều phòng một lần, để không truy vấn từng phòng.
     * Lấy chỉ số CONFIRMED gần nhất trước kỳ. Nếu chưa có kỳ nào của hợp đồng đang mở (hóa đơn đầu tiên)
     * thì dùng chỉ số ban đầu ghi lúc nhận phòng (BR-18 RULE 5); không có cả hai thì null.
     *
     * @author Tuấn
     * @version 1.1
     */
    public async Task<Dictionary<(Guid RoomId, MeterKind Kind), long?>> PreviousValuesAsync(List<Guid> roomIds, string period, CancellationToken ct)
    {
        var rows = await db.MeterReadings.AsNoTracking()
            .Where(m => roomIds.Contains(m.RoomId) && m.Status == MeterReadingStatus.Confirmed && string.Compare(m.Period, period) < 0)
            .GroupBy(m => new { m.RoomId, m.Kind })
            .Select(g => g.OrderByDescending(m => m.Period).Select(m => new { m.RoomId, m.Kind, m.TenancyId, m.Value }).First())
            .ToListAsync(ct);
        var openings = await db.Tenancies.AsNoTracking()
            .Where(t => roomIds.Contains(t.RoomId) && t.Status != TenancyStatus.Settled)
            .Select(t => new { t.Id, t.RoomId, t.OpeningElectricity, t.OpeningWater })
            .ToListAsync(ct);

        var result = new Dictionary<(Guid RoomId, MeterKind Kind), long?>();
        foreach (var roomId in roomIds.Distinct())
        {
            var tenancy = openings.FirstOrDefault(t => t.RoomId == roomId);
            foreach (var kind in new[] { MeterKind.Electricity, MeterKind.Water })
            {
                var last = rows.FirstOrDefault(r => r.RoomId == roomId && r.Kind == kind);
                var opening = kind == MeterKind.Electricity ? tenancy?.OpeningElectricity : tenancy?.OpeningWater;
                // Chỉ số cũ của người thuê trước không dùng khi hợp đồng mới đã ghi chỉ số ban đầu
                var value = last is not null && (tenancy is null || last.TenancyId == tenancy.Id || opening is null)
                    ? last.Value
                    : opening;
                if (value is not null)
                {
                    result[(roomId, kind)] = value;
                }
            }
        }

        return result;
    }

    /**
     * Đổi chỉ số đã xác nhận rồi dùng cho bản ghi response.
     *
     * @author Tuấn
     * @version 1.0
     */
    public MeterReadingResponse ToResponse(MeterReading m, long? previous) => new(
        m.Id, m.RoomId, m.Kind, m.Period, m.Status, m.Value ?? m.AiValue, m.AiValue, m.AiConfidence, previous,
        m.Status == MeterReadingStatus.NeedsReview, m.ReviewReason, m.MeterReplaced, m.Note, m.EditedByLandlord, m.ConfirmedAt,
        m.PhotoStatus,
        m.PhotoKey is not null && m.PhotoStatus is not (null or FileStatus.PendingUpload)
            ? storage.CreateDownloadUrl(StorageBucket.Incoming, m.PhotoKey, DownloadLifetime)
            : null);

    private async Task<MeterReading> GetOwnedAsync(Guid readingId, CancellationToken ct)
    {
        var landlordId = user.UserId;
        return await db.MeterReadings.FirstOrDefaultAsync(m => m.Id == readingId && m.Tenancy!.LandlordId == landlordId, ct)
            ?? throw new NotFoundException("Không tìm thấy chỉ số");
    }

    private void Confirm(MeterReading reading, long? previous)
    {
        MeterReadingService.ValidateConfirmedValue(reading.Value ?? reading.AiValue, previous, reading.MeterReplaced);
        reading.Value ??= reading.AiValue;
        reading.Status = MeterReadingStatus.Confirmed;
        reading.ConfirmedBy = user.UserId;
        reading.ConfirmedAt = db.Now;
        db.Audit(AuditEventTypes.MeterReadingConfirmed, nameof(MeterReading), reading.Id);
    }
}
