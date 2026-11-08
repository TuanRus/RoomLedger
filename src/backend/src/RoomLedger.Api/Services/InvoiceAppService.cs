using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Nghiệp vụ hóa đơn và khiếu nại chỉ số (BR-22 đến BR-24).
 *
 * @author Tuấn
 * @version 1.0
 */
public class InvoiceAppService(AppDbContext db, ICurrentUser user, IEventPublisher events, MeterAppService meters, PropertyAppService properties)
{
    public const string SkipMissingReadings = "MISSING_READINGS";
    public const string SkipMissingPrevious = "MISSING_PREVIOUS_READING";
    public const string SkipNoLockedContract = "NO_LOCKED_CONTRACT";
    public const string SkipAlreadyInvoiced = "ALREADY_INVOICED";

    /**
     * Lập hóa đơn nháp hàng loạt cho kỳ (BR-22). Phòng thiếu chỉ số xác nhận hoặc đã có hóa đơn được trả trong skipped.
     * Unique index (tenancy_id, period) chặn trùng khi hai lần bấm chạy cùng lúc.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<GenerateInvoicesResponse> GenerateAsync(Guid propertyId, GenerateInvoicesRequest req, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        var period = Period.Parse(req.Period);
        InvoiceService.EnsurePeriodStarted(period, DateOnly.FromDateTime(db.Now.UtcDateTime));

        var tenancies = await db.Tenancies.AsNoTracking()
            .Where(t => t.Room!.PropertyId == property.Id && (t.Status == TenancyStatus.Active || t.Status == TenancyStatus.MovingOut))
            .OrderBy(t => t.Room!.Code)
            .Select(t => new
            {
                t.Id,
                t.RoomId,
                t.Room!.Code,
                Terms = t.Versions.Where(v => v.Status == TenancyVersionStatus.Locked).OrderByDescending(v => v.Number)
                    .Select(v => new { v.MonthlyRent, v.ElectricityRate, v.WaterRate }).FirstOrDefault(),
                Invoiced = db.Invoices.Any(i => i.TenancyId == t.Id && i.Period == period),
            })
            .ToListAsync(ct);
        var roomIds = tenancies.Select(t => t.RoomId).ToList();
        var readings = await db.MeterReadings.AsNoTracking()
            .Where(m => roomIds.Contains(m.RoomId) && m.Period == period && m.Status == MeterReadingStatus.Confirmed)
            .ToListAsync(ct);
        var previous = await meters.PreviousValuesAsync(roomIds, period, ct);

        var created = new List<GeneratedInvoice>();
        var skipped = new List<SkippedRoom>();
        foreach (var t in tenancies)
        {
            var elec = readings.FirstOrDefault(m => m.RoomId == t.RoomId && m.Kind == MeterKind.Electricity);
            var water = readings.FirstOrDefault(m => m.RoomId == t.RoomId && m.Kind == MeterKind.Water);
            var elecPrev = previous.GetValueOrDefault((t.RoomId, MeterKind.Electricity));
            var waterPrev = previous.GetValueOrDefault((t.RoomId, MeterKind.Water));
            string? reason = t.Invoiced ? SkipAlreadyInvoiced
                : t.Terms?.MonthlyRent is null || t.Terms.ElectricityRate is null || t.Terms.WaterRate is null ? SkipNoLockedContract
                : elec?.Value is null || water?.Value is null ? SkipMissingReadings
                : elecPrev is null || waterPrev is null ? SkipMissingPrevious
                : null;
            if (reason is not null)
            {
                skipped.Add(new SkippedRoom(t.RoomId, t.Code, reason));
                continue;
            }

            var invoice = new Invoice
            {
                TenancyId = t.Id,
                Period = period,
                ElectricityReadingId = elec!.Id,
                WaterReadingId = water!.Id,
            };
            InvoiceService.Calculate(invoice, new InvoiceInput(
                t.Terms!.MonthlyRent!.Value,
                elec.MeterReplaced ? 0 : elecPrev!.Value, elec.Value!.Value, t.Terms.ElectricityRate!.Value,
                water.MeterReplaced ? 0 : waterPrev!.Value, water.Value!.Value, t.Terms.WaterRate!.Value,
                property.ServiceFee));
            db.Invoices.Add(invoice);
            try
            {
                await db.SaveChangesAsync(ct);
                created.Add(new GeneratedInvoice(invoice.Id, t.RoomId, t.Code, invoice.Total));
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Request khác vừa lập hóa đơn cho kỳ này: giống ON CONFLICT DO NOTHING
                db.Entry(invoice).State = EntityState.Detached;
                skipped.Add(new SkippedRoom(t.RoomId, t.Code, SkipAlreadyInvoiced));
            }
        }

        db.Audit(AuditEventTypes.InvoicesGenerated, nameof(Property), property.Id,
            System.Text.Json.JsonSerializer.Serialize(new { period, created = created.Count, skipped = skipped.Count }));
        await db.SaveChangesAsync(ct);
        return new GenerateInvoicesResponse(created, skipped);
    }

    /**
     * Danh sách hóa đơn: chủ nhà thấy hóa đơn của mình, người thuê chỉ thấy hóa đơn đã gửi của hợp đồng mình (BR-23 RULE 2).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PagedResult<InvoiceResponse>> ListAsync(string? period, InvoiceStatus? status, int? limit, string? cursor, CancellationToken ct)
    {
        var take = Paging.NormalizeLimit(limit);
        var query = db.Invoices.AsNoTracking().AsQueryable();
        if (user.Role == UserRole.Tenant)
        {
            query = query.Where(i => i.Status != InvoiceStatus.Draft);
        }

        if (period is not null)
        {
            var p = Period.Parse(period);
            query = query.Where(i => i.Period == p);
        }

        if (status is not null)
        {
            query = query.Where(i => i.Status == status);
        }

        var rows = await query.Keyset(PageCursor.Decode(cursor), take)
            .Select(i => new { Invoice = i, RoomCode = i.Tenancy!.Room!.Code, PropertyName = i.Tenancy.Room.Property!.Name })
            .ToListAsync(ct);
        return Paging.ToPage(rows, take, r => new PageCursor(r.Invoice.CreatedAt, r.Invoice.Id), r => new InvoiceResponse(
            r.Invoice.Id, r.Invoice.TenancyId, r.RoomCode, r.PropertyName, r.Invoice.Period, r.Invoice.Status,
            r.Invoice.Total, r.Invoice.SentAt, r.Invoice.PaidAt));
    }

    /**
     * Chi tiết hóa đơn kèm ảnh đồng hồ (presigned GET 5 phút) và khiếu nại.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<InvoiceDetailResponse> GetAsync(Guid invoiceId, CancellationToken ct)
    {
        var row = await VisibleInvoices()
            .Where(i => i.Id == invoiceId)
            .Select(i => new { Invoice = i, RoomCode = i.Tenancy!.Room!.Code, PropertyName = i.Tenancy.Room.Property!.Name })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Không tìm thấy hóa đơn");
        var inv = row.Invoice;
        var readings = await db.MeterReadings.AsNoTracking()
            .Where(m => m.Id == inv.ElectricityReadingId || m.Id == inv.WaterReadingId)
            .ToDictionaryAsync(m => m.Id, ct);
        var disputes = await db.Disputes.AsNoTracking()
            .Where(d => d.InvoiceId == inv.Id)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

        string? Photo(Guid id) => readings.TryGetValue(id, out var m) ? meters.ToResponse(m, null).PhotoUrl : null;
        return new InvoiceDetailResponse(
            inv.Id, inv.TenancyId, row.RoomCode, row.PropertyName, inv.Period, inv.Status, inv.RoomRent,
            new UtilityLine(inv.ElectricityReadingId, inv.ElectricityPrevious, inv.ElectricityCurrent,
                inv.ElectricityCurrent - inv.ElectricityPrevious, inv.ElectricityRate, inv.ElectricityAmount, Photo(inv.ElectricityReadingId)),
            new UtilityLine(inv.WaterReadingId, inv.WaterPrevious, inv.WaterCurrent,
                inv.WaterCurrent - inv.WaterPrevious, inv.WaterRate, inv.WaterAmount, Photo(inv.WaterReadingId)),
            inv.ServiceFee, inv.Total, inv.SentAt, inv.PaidAt,
            disputes.Select(ToDisputeResponse).ToList());
    }

    /**
     * Gửi hóa đơn cho người thuê (BR-23 RULE 1), phát InvoiceSent để notify gửi SES.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<InvoiceResponse> SendAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await GetOwnedAsync(invoiceId, ct);
        InvoiceService.Send(invoice, db.Now);
        db.Audit(AuditEventTypes.InvoiceSent, nameof(Invoice), invoice.Id);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync("InvoiceSent", new { invoiceId = invoice.Id, tenancyId = invoice.TenancyId, tenantId = invoice.Tenancy!.TenantId }, ct);
        return await SummaryAsync(invoice.Id, ct);
    }

    /**
     * Đánh dấu đã thu tiền (BR-24). Hệ thống không xử lý thanh toán online.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<InvoiceResponse> MarkPaidAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await GetOwnedAsync(invoiceId, ct);
        InvoiceService.MarkPaid(invoice, db.Now);
        db.Audit(AuditEventTypes.InvoicePaid, nameof(Invoice), invoice.Id);
        await db.SaveChangesAsync(ct);
        return await SummaryAsync(invoice.Id, ct);
    }

    /**
     * Người thuê khiếu nại một chỉ số của hóa đơn (BR-23 RULE 3): tạo Dispute, hóa đơn chuyển DISPUTED.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<DisputeResponse> CreateDisputeAsync(Guid invoiceId, CreateDisputeRequest req, CancellationToken ct)
    {
        var tenantId = user.UserId;
        var invoice = await db.Invoices.Include(i => i.Tenancy)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.Tenancy!.TenantId == tenantId && i.Status != InvoiceStatus.Draft, ct)
            ?? throw new NotFoundException("Không tìm thấy hóa đơn");
        var reason = DisputeService.ValidateReason(req.Reason);
        if (req.ReadingId is null || (req.ReadingId != invoice.ElectricityReadingId && req.ReadingId != invoice.WaterReadingId))
        {
            throw ValidationException.For("readingId", "Chỉ số không thuộc hóa đơn này");
        }

        InvoiceService.MarkDisputed(invoice);
        var dispute = new Dispute
        {
            TenancyId = invoice.TenancyId,
            TargetType = DisputeTargetType.MeterReading,
            TargetId = req.ReadingId.Value,
            InvoiceId = invoice.Id,
            RaisedBy = tenantId,
            Reason = reason,
        };
        db.Disputes.Add(dispute);
        db.Audit(AuditEventTypes.InvoiceDisputed, nameof(Invoice), invoice.Id);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync("InvoiceDisputed", new { invoiceId = invoice.Id, disputeId = dispute.Id, landlordId = invoice.Tenancy!.LandlordId }, ct);
        return ToDisputeResponse(dispute);
    }

    /**
     * Chủ nhà xử lý khiếu nại chỉ số (BR-23 RULE 4): sửa chỉ số thì tính lại hóa đơn, hoặc giữ nguyên kèm phản hồi.
     * Hết khiếu nại mở thì hóa đơn quay về SENT.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<DisputeResponse> ResolveDisputeAsync(Guid disputeId, ResolveDisputeRequest req, CancellationToken ct)
    {
        var landlordId = user.UserId;
        var dispute = await db.Disputes.FirstOrDefaultAsync(d => d.Id == disputeId && d.Tenancy!.LandlordId == landlordId
            && d.TargetType == DisputeTargetType.MeterReading, ct)
            ?? throw new NotFoundException("Không tìm thấy khiếu nại");
        var resolution = req.Resolution ?? throw ValidationException.For("resolution", "Hãy chọn CORRECTED hoặc KEPT");
        var invoice = await db.Invoices.FirstAsync(i => i.Id == dispute.InvoiceId, ct);

        if (resolution == DisputeResolution.Corrected)
        {
            var reading = await db.MeterReadings.FirstAsync(m => m.Id == dispute.TargetId, ct);
            var previous = reading.Kind == MeterKind.Electricity ? invoice.ElectricityPrevious : invoice.WaterPrevious;
            MeterReadingService.ValidateConfirmedValue(req.Value, previous, reading.MeterReplaced);
            reading.Value = req.Value;
            reading.EditedByLandlord = true;
            var input = new InvoiceInput(invoice.RoomRent,
                invoice.ElectricityPrevious, reading.Kind == MeterKind.Electricity ? req.Value!.Value : invoice.ElectricityCurrent, invoice.ElectricityRate,
                invoice.WaterPrevious, reading.Kind == MeterKind.Water ? req.Value!.Value : invoice.WaterCurrent, invoice.WaterRate,
                invoice.ServiceFee);
            InvoiceService.Calculate(invoice, input);
        }

        DisputeService.Resolve(dispute, resolution, req.Response, db.Now);
        var otherOpen = await db.Disputes.AnyAsync(d => d.InvoiceId == invoice.Id && d.Id != dispute.Id && d.Status == DisputeStatus.Open, ct);
        if (!otherOpen && invoice.Status == InvoiceStatus.Disputed)
        {
            invoice.Status = InvoiceStatus.Sent;
        }

        db.Audit(AuditEventTypes.DisputeResolved, nameof(Dispute), dispute.Id);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync("DisputeResolved", new { disputeId = dispute.Id, invoiceId = invoice.Id, tenancyId = invoice.TenancyId }, ct);
        return ToDisputeResponse(dispute);
    }

    private IQueryable<Invoice> VisibleInvoices() =>
        user.Role == UserRole.Tenant
            ? db.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Draft)
            : db.Invoices.AsNoTracking();

    private async Task<Invoice> GetOwnedAsync(Guid invoiceId, CancellationToken ct)
    {
        var landlordId = user.UserId;
        return await db.Invoices.Include(i => i.Tenancy)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.Tenancy!.LandlordId == landlordId, ct)
            ?? throw new NotFoundException("Không tìm thấy hóa đơn");
    }

    private async Task<InvoiceResponse> SummaryAsync(Guid invoiceId, CancellationToken ct) =>
        await db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId)
            .Select(i => new InvoiceResponse(i.Id, i.TenancyId, i.Tenancy!.Room!.Code, i.Tenancy.Room.Property!.Name,
                i.Period, i.Status, i.Total, i.SentAt, i.PaidAt))
            .FirstAsync(ct);

    private static DisputeResponse ToDisputeResponse(Dispute d) =>
        new(d.Id, d.InvoiceId ?? Guid.Empty, d.TargetId, d.Reason, d.Status, d.Resolution, d.Response, d.CreatedAt, d.ResolvedAt);
}
