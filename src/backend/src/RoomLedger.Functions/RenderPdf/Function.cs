using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.RenderPdf;

/**
 * Lambda render-pdf (MD-06-FE-05, BR-29), chạy trong VPC (đọc RDS, ghi S3 qua gateway endpoint). Nghe TenancySettled:
 * đọc quyết toán đã chốt, hai bên (app_user), nhà và phòng, các khoản đã trừ (ACCEPTED, RESOLVED) với tên khu vực theo
 * bản sao của biên bản trả phòng, các hóa đơn chưa PAID; dựng biên bản thanh lý PDF, ghi vào bucket derived ở
 * reports/{tenancyId}/thanh-ly-{settlementId}.pdf rồi điền settlement.pdf_key (UPDATE có điều kiện pdf_key IS NULL) và
 * audit SETTLEMENT_PDF_CREATED. Đã có pdf_key (EventBridge gửi trùng) hoặc không tìm thấy quyết toán → bỏ qua.
 * Lỗi S3/database → ném lỗi để EventBridge thử lại, hết lượt vào DLQ.
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

    /** Điểm vào Lambda: nhận "Object Created" của bucket outbox. */
    public async Task HandleOutboxAsync(CloudWatchEvent<S3ObjectCreatedDetail> input, ILambdaContext context) =>
        await HandleAsync(await OutboxEvent.ReadAsync<TenancySettledDetail>(_store, input, CancellationToken.None), context);

    public async Task HandleAsync(CloudWatchEvent<TenancySettledDetail> input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        var detail = input.Detail;
        await using var db = _db();
        var settlement = await db.Settlements.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == detail.SettlementId && s.TenancyId == detail.TenancyId, ct);
        if (settlement is null || settlement.PdfKey is not null)
        {
            context.Logger.LogInformation($"Bỏ qua quyết toán {detail.SettlementId}: {(settlement is null ? "không tồn tại" : "đã có PDF")}");
            return;
        }

        var report = await LoadReportAsync(db, settlement, ct);
        var pdf = SettlementDocument.Render(report);
        var key = $"reports/{settlement.TenancyId}/thanh-ly-{settlement.Id}.pdf";
        await _store.PutAsync(StorageBucket.Derived, key, pdf, "application/pdf", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Settlements.Where(s => s.Id == settlement.Id && s.PdfKey == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PdfKey, key).SetProperty(x => x.UpdatedAt, db.Now), ct);
        if (rows == 1)
        {
            db.Audit(AuditEventTypes.SettlementPdfCreated, nameof(Core.Domain.Settlement), settlement.Id, $$"""{"key":"{{key}}","bytes":{{pdf.Length}}}""");
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        context.Logger.LogInformation($"Quyết toán {settlement.Id}: biên bản thanh lý {key} ({pdf.Length} byte)");
    }

    private static async Task<SettlementReport> LoadReportAsync(AppDbContext db, Core.Domain.Settlement s, CancellationToken ct)
    {
        var tenancy = await db.Tenancies.AsNoTracking().Include(t => t.Room!).ThenInclude(r => r.Property)
            .SingleAsync(t => t.Id == s.TenancyId, ct);
        var userIds = new[] { tenancy.LandlordId, tenancy.TenantId ?? Guid.Empty };
        var users = await db.AppUsers.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        string NameOf(Guid? id) => id is { } v && users.TryGetValue(v, out var u)
            ? (string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName)
            : "—";

        var inspection = await db.Inspections.AsNoTracking().SingleAsync(i => i.Id == s.InspectionId, ct);
        var areaNames = InspectionService.ParseSnapshot(inspection.AreaSnapshotJson).ToDictionary(a => a.RoomAreaId, a => a.Name);
        var findings = await db.Findings.AsNoTracking()
            .Where(f => f.InspectionId == s.InspectionId && (f.Status == FindingStatus.Accepted || f.Status == FindingStatus.Resolved))
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).ToListAsync(ct);
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.TenancyId == s.TenancyId && i.Status != InvoiceStatus.Paid)
            .OrderBy(i => i.Period).ToListAsync(ct);

        return new SettlementReport(
            s.Id,
            tenancy.Room!.Property!.Name,
            tenancy.Room.Property.Address,
            tenancy.Room.Code,
            NameOf(tenancy.LandlordId),
            NameOf(tenancy.TenantId),
            inspection.FinalPeriod ?? InspectionService.FinalPeriod(inspection.CreatedAt),
            s.SettledAt,
            s.Deposit,
            s.DeductionsTotal,
            s.UnpaidInvoicesTotal,
            s.Refund,
            findings.Select(f => new DeductionLine(areaNames.GetValueOrDefault(f.RoomAreaId, "—"), f.Title, f.Amount ?? 0,
                f.Status == FindingStatus.Resolved)).ToList(),
            invoices.Select(i => new InvoiceLine(i.Period.Trim(), i.Status, i.Total)).ToList());
    }
}
