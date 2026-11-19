using System.Text.Json;
using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.PersistResults;

/**
 * Bước cuối Step Functions so ảnh trả phòng (MD-06-FE-02, BR-26). Chạy trong VPC (ghi RDS), trong một transaction:
 * UPDATE có điều kiện COMPARING → REVIEWING (0 dòng = đã ghi rồi hoặc biên bản đổi trạng thái → không làm gì), rồi ghi
 * bảng finding: mỗi phát hiện AI một dòng SUGGESTED; cặp gần giống hệt một dòng SKIPPED; cặp không phân tích được
 * (không có ảnh nhận phòng, vượt giới hạn gọi, AI lỗi hoặc Map không trả kết quả) một dòng NOT_ANALYZED kèm lý do để
 * chủ nhà xem tay. Cặp AI xem mà không thấy gì thì không ghi dòng. Audit MOVE_OUT_COMPARED kèm số đếm.
 *
 * @author Sang
 * @version 1.0
 */
public class Function
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<AppDbContext> _db;

    public Function() : this(FunctionDb.Create)
    {
    }

    public Function(Func<AppDbContext> db) => _db = db;

    public async Task<PersistOutput> HandleAsync(PersistInput input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        await using var db = _db();
        var tenancyId = await db.Inspections.AsNoTracking().Where(i => i.Id == input.InspectionId)
            .Select(i => (Guid?)i.TenancyId).SingleOrDefaultAsync(ct);
        var nothing = new PersistOutput(false, input.InspectionId, tenancyId ?? Guid.Empty, 0, 0, 0, 0, 0);
        if (tenancyId is null)
        {
            context.Logger.LogWarning($"Biên bản {input.InspectionId} không tồn tại");
            return nothing;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var moved = await db.Inspections
            .Where(i => i.Id == input.InspectionId && i.Status == InspectionStatus.Comparing)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, InspectionStatus.Reviewing), ct);
        if (moved == 0)
        {
            context.Logger.LogInformation($"Biên bản {input.InspectionId} không còn COMPARING, bỏ qua");
            return nothing;
        }

        var results = input.Results.OfType<PairResult>().GroupBy(r => r.MoveOutPhotoId).ToDictionary(g => g.Key, g => g.First());
        int analyzed = 0, suggested = 0, skipped = 0, notAnalyzed = 0, fallback = 0;
        foreach (var pair in input.Pairs)
        {
            Finding Row(FindingStatus status, string title, string? description) => new()
            {
                InspectionId = input.InspectionId, RoomAreaId = pair.AreaId, MoveInPhotoId = pair.MoveInPhotoId,
                MoveOutPhotoId = pair.MoveOutPhotoId, PhashDistance = pair.Distance, Status = status, Title = title, Description = description,
            };

            if (pair.Decision == PairDecision.Skipped)
            {
                db.Findings.Add(Row(FindingStatus.Skipped, "Không thấy thay đổi", $"Ảnh gần giống ảnh nhận phòng (khoảng cách {pair.Distance})."));
                skipped++;
            }
            else if (pair.Decision == PairDecision.Compare && results.TryGetValue(pair.MoveOutPhotoId, out var r) && r.Status == PairResult.Analyzed)
            {
                analyzed++;
                fallback += r.UsedFallback ? 1 : 0;
                foreach (var f in r.Findings ?? [])
                {
                    var row = Row(FindingStatus.Suggested, f.Title, f.Description);
                    row.BoundingBoxJson = JsonSerializer.Serialize(f.BoundingBox, Json);
                    row.Confidence = Math.Round((decimal)f.Confidence, 3);
                    row.ModelId = r.ModelId;
                    row.UsedFallback = r.UsedFallback;
                    db.Findings.Add(row);
                    suggested++;
                }
            }
            else
            {
                db.Findings.Add(Row(FindingStatus.NotAnalyzed, "Chưa phân tích tự động", NotAnalyzedReason(pair)));
                notAnalyzed++;
            }
        }

        db.Audit(AuditEventTypes.MoveOutCompared, nameof(Inspection), input.InspectionId,
            $$"""{"analyzed":{{analyzed}},"suggested":{{suggested}},"skipped":{{skipped}},"notAnalyzed":{{notAnalyzed}},"usedFallback":{{fallback}}}""");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        context.Logger.LogInformation($"Biên bản {input.InspectionId} → REVIEWING: {suggested} gợi ý, {skipped} bỏ qua, {notAnalyzed} chưa phân tích");
        return new PersistOutput(true, input.InspectionId, tenancyId.Value, analyzed, suggested, skipped, notAnalyzed, fallback);
    }

    private static string NotAnalyzedReason(PlannedPair pair) => pair.Reason switch
    {
        ComparePlanner.NoMoveInPhoto => "Không có ảnh nhận phòng cùng khu vực để so, cần xem tay.",
        ComparePlanner.CallLimit => "Vượt giới hạn số lần so sánh tự động của biên bản, cần xem tay.",
        _ => "AI không phân tích được ảnh này, cần xem tay.",
    };
}
