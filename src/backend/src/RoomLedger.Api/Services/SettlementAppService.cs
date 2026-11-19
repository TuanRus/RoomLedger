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
 * Quyết toán trả phòng (MD-06-FE-05, BR-29; quyết định A2, B3–B5 của Tuấn ngày 17/11). Chỉ sau khi chủ nhà đã gửi
 * danh sách trừ cọc cho người thuê (biên bản trả phòng AWAITING_CONFIRM).
 * Xem trước: hai bên, tính lại mỗi lần, kèm điều kiện còn thiếu. Quyết toán: chỉ chủ nhà; trong một transaction đổi
 * biên bản AWAITING_CONFIRM → SETTLED trước (giữ khóa dòng, lần bấm thứ hai chờ rồi thấy đã quyết toán), tính số và
 * kiểm điều kiện sau khóa (thiếu → rollback, 409 mã đầu tiên, details liệt kê tất cả), ghi settlement, hợp đồng
 * MOVING_OUT → SETTLED, phòng → VACANT, audit. Sau commit phát TenancySettled (biên bản PDF, niêm phong ảnh, email).
 * 1.1: trả pdfUrl (link tải biên bản thanh lý do render-pdf tạo).
 *
 * @author Sang
 * @version 1.1
 */
public class SettlementAppService(AppDbContext db, ICurrentUser user, TimeProvider clock, IEventPublisher events, IStorage storage)
{
    private static readonly TimeSpan PdfLinkLifetime = TimeSpan.FromMinutes(5);

    private sealed record Figures(string Period, long Deposit, long Deductions, long Unpaid, IReadOnlyList<SettlementBlocker> Blockers)
    {
        public long Refund => SettlementService.Refund(Deposit, Deductions, Unpaid);
    }

    private sealed record InspectionView(Guid Id, InspectionStatus Status, string? FinalPeriod, DateTimeOffset CreatedAt)
    {
        public string Period => FinalPeriod ?? InspectionService.FinalPeriod(CreatedAt);
    }

    /**
     * Xem quyết toán: đã quyết toán → số đã chốt; chưa → số tính hiện tại và các điều kiện còn thiếu.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<SettlementResponse> GetAsync(Guid tenancyId, CancellationToken ct)
    {
        var (tenancy, inspection) = await LoadAsync(tenancyId, ct);
        var existing = await db.Settlements.AsNoTracking().FirstOrDefaultAsync(s => s.TenancyId == tenancyId, ct);
        if (existing is not null)
        {
            return ToResponse(existing, inspection);
        }
        EnsureSentToTenant(inspection.Status);
        var figures = await ComputeAsync(tenancy, inspection, ct);
        return new SettlementResponse(tenancyId, inspection.Id, figures.Period, SettlementResponse.Pending,
            figures.Deposit, figures.Deductions, figures.Unpaid, figures.Refund, figures.Blockers.Count == 0, figures.Blockers);
    }

    /**
     * Quyết toán (chủ nhà). Đã quyết toán → trả lại quyết toán đó (Created = false), không ghi hay báo lại.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<(SettlementResponse Settlement, bool Created)> SettleAsync(Guid tenancyId, CancellationToken ct)
    {
        var (tenancy, inspection) = await LoadAsync(tenancyId, ct);
        if (user.UserId != tenancy.LandlordId)
        {
            throw new ForbiddenException("Chỉ chủ nhà mới quyết toán được");
        }

        var now = clock.GetUtcNow();
        Settlement settlement;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.Inspections.Where(i => i.Id == inspection.Id && i.Status == InspectionStatus.AwaitingConfirm)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InspectionStatus.Settled).SetProperty(i => i.UpdatedAt, now), ct);
            if (rows == 0)
            {
                var current = await db.Inspections.Where(i => i.Id == inspection.Id).Select(i => i.Status).SingleAsync(ct);
                if (current == InspectionStatus.Settled
                    && await db.Settlements.AsNoTracking().FirstOrDefaultAsync(s => s.TenancyId == tenancyId, ct) is { } done)
                {
                    return (ToResponse(done, inspection), false);   // bấm lần hai hoặc chờ lần kia xong
                }
                EnsureSentToTenant(current);
                throw new InvalidStateException("Biên bản trả phòng không ở bước quyết toán");
            }

            var figures = await ComputeAsync(tenancy, inspection, ct);
            if (figures.Blockers.Count > 0)
            {
                throw new ConflictException(figures.Blockers[0].Code, string.Join("; ", figures.Blockers.Select(b => b.Message)),
                    figures.Blockers.Cast<object>().ToList());
            }

            settlement = new Settlement
            {
                TenancyId = tenancyId,
                InspectionId = inspection.Id,
                Deposit = figures.Deposit,
                DeductionsTotal = figures.Deductions,
                UnpaidInvoicesTotal = figures.Unpaid,
                Refund = figures.Refund,
                SettledAt = now,
                SettledBy = user.UserId,
            };
            db.Settlements.Add(settlement);
            await db.Tenancies.Where(t => t.Id == tenancyId && t.Status == TenancyStatus.MovingOut)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenancyStatus.Settled).SetProperty(t => t.UpdatedAt, now), ct);
            await db.Rooms.Where(r => r.Id == tenancy.RoomId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RoomStatus.Vacant).SetProperty(r => r.UpdatedAt, now), ct);
            db.Audit(AuditEventTypes.TenancySettled, nameof(Tenancy), tenancyId,
                $$"""{"deposit":{{figures.Deposit}},"deductions":{{figures.Deductions}},"unpaidInvoices":{{figures.Unpaid}},"refund":{{figures.Refund}}}""");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await events.PublishAsync("TenancySettled", new
        {
            tenancyId,
            settlementId = settlement.Id,
            inspectionId = inspection.Id,
            refund = settlement.Refund,
            notifyUserId = tenancy.TenantId,
        }, ct);
        return (ToResponse(settlement, inspection), true);
    }

    /** Hợp đồng (404 nếu không phải của người gọi) và biên bản trả phòng của nó (chưa có → 409 INVALID_STATE). */
    private async Task<(Tenancy Tenancy, InspectionView Inspection)> LoadAsync(Guid tenancyId, CancellationToken ct)
    {
        var tenancy = await db.Tenancies.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenancyId, ct)
            ?? throw new NotFoundException("Không tìm thấy hợp đồng");
        var inspection = await db.Inspections.AsNoTracking()
            .Where(i => i.TenancyId == tenancyId && i.Type == InspectionType.MoveOut)
            .Select(i => new InspectionView(i.Id, i.Status, i.FinalPeriod, i.CreatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidStateException("Hợp đồng chưa có biên bản trả phòng");
        return (tenancy, inspection);
    }

    private static void EnsureSentToTenant(InspectionStatus status)
    {
        if (status is not (InspectionStatus.AwaitingConfirm or InspectionStatus.Settled))
        {
            throw new InvalidStateException("Chủ nhà chưa gửi danh sách trừ cọc cho người thuê");
        }
    }

    /**
     * Số quyết toán và điều kiện: cọc của phiên bản hợp đồng khóa mới nhất; trừ = mục ACCEPTED + RESOLVED; chưa trả =
     * mọi hóa đơn khác PAID; chỉ số điện nước kỳ trả phòng phải CONFIRMED; phải có hóa đơn kỳ trả phòng; khiếu nại mở =
     * mục DISPUTED + hóa đơn DISPUTED; mục CHARGED = người thuê chưa trả lời.
     */
    private async Task<Figures> ComputeAsync(Tenancy tenancy, InspectionView inspection, CancellationToken ct)
    {
        var period = inspection.Period;
        var deposit = await db.TenancyVersions
            .Where(v => v.TenancyId == tenancy.Id && v.Status == TenancyVersionStatus.Locked)
            .OrderByDescending(v => v.Number).Select(v => v.Deposit).FirstOrDefaultAsync(ct) ?? 0;

        var findings = await db.Findings.Where(f => f.InspectionId == inspection.Id)
            .Select(f => new { f.Status, f.Amount }).ToListAsync(ct);
        var invoices = await db.Invoices.Where(i => i.TenancyId == tenancy.Id)
            .Select(i => new { i.Period, i.Status, i.Total }).ToListAsync(ct);
        var confirmed = await db.MeterReadings
            .Where(m => m.TenancyId == tenancy.Id && m.Period == period && m.Status == MeterReadingStatus.Confirmed)
            .Select(m => m.Kind).ToListAsync(ct);

        var check = new SettlementCheck(
            period,
            new[] { MeterKind.Electricity, MeterKind.Water }.Where(k => !confirmed.Contains(k)).ToList(),
            invoices.Any(i => i.Period.Trim() == period),
            findings.Count(f => f.Status == FindingStatus.Disputed),
            invoices.Count(i => i.Status == InvoiceStatus.Disputed),
            findings.Count(f => f.Status == FindingStatus.Charged));
        return new Figures(
            period,
            deposit,
            findings.Where(f => f.Status is FindingStatus.Accepted or FindingStatus.Resolved).Sum(f => f.Amount ?? 0),
            invoices.Where(i => i.Status != InvoiceStatus.Paid).Sum(i => i.Total),
            SettlementService.Blockers(check));
    }

    /** Số đã chốt; PdfUrl = link tải biên bản thanh lý 5 phút, null tới khi render-pdf ghi xong pdf_key. */
    private SettlementResponse ToResponse(Settlement s, InspectionView inspection) => new(
        s.TenancyId, s.InspectionId, inspection.Period, SettlementResponse.Settled,
        s.Deposit, s.DeductionsTotal, s.UnpaidInvoicesTotal, s.Refund, CanSettle: false, Blockers: [],
        SettlementId: s.Id, SettledAt: s.SettledAt, SettledBy: s.SettledBy,
        PdfUrl: s.PdfKey is null ? null : storage.CreateDownloadUrl(StorageBucket.Derived, s.PdfKey, PdfLinkLifetime));
}
