using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Chủ nhà duyệt gợi ý trả phòng (MD-06-FE-03, BR-27). Chỉ khi biên bản trả phòng REVIEWING.
 * Mọi thay đổi khóa dòng inspection trước (UPDATE có điều kiện status = REVIEWING) nên không chen được vào lúc gửi
 * người thuê. Tổng tiền các mục CHARGED ≤ tiền cọc của phiên bản hợp đồng đã khóa mới nhất (cách hiểu của Sang:
 * điện nước tháng cuối là hóa đơn riêng, trừ ở quyết toán, nên không cộng vào giới hạn này).
 * Người thuê chỉ thấy các mục bị tính tiền sau khi chủ nhà đã gửi (AWAITING_CONFIRM trở đi).
 *
 * @author Sang
 * @version 1.0
 */
public class FindingAppService(AppDbContext db, ICurrentUser user, TimeProvider clock, IEventPublisher events)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly FindingStatus[] VisibleToTenant =
        [FindingStatus.Charged, FindingStatus.Accepted, FindingStatus.Disputed, FindingStatus.Resolved];
    private static readonly FindingStatus[] Deductible = VisibleToTenant;

    /**
     * Danh sách mục của biên bản. Chủ nhà thấy tất cả; người thuê chỉ thấy mục bị tính tiền sau khi đã gửi.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<IReadOnlyList<FindingResponse>> ListAsync(Guid inspectionId, CancellationToken ct)
    {
        var inspection = await LoadInspectionAsync(inspectionId, ct);
        var query = db.Findings.AsNoTracking().Where(f => f.InspectionId == inspectionId);
        if (user.UserId != inspection.Tenancy!.LandlordId)
        {
            if (inspection.Status is not (InspectionStatus.AwaitingConfirm or InspectionStatus.Settled))
            {
                return [];
            }
            query = query.Where(f => VisibleToTenant.Contains(f.Status));
        }
        var findings = await query.OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).ToListAsync(ct);
        return findings.Select(ToResponse).ToList();
    }

    /** Tính tiền (BR-27 RULE 1). */
    public Task<FindingResponse> ChargeAsync(Guid findingId, ChargeFindingRequest req, CancellationToken ct)
    {
        new Validator().Positive(req.Amount, "amount", "Số tiền").ThrowIfInvalid();
        return DecideAsync(findingId, FindingStatus.Charged, req.Amount, AuditEventTypes.FindingCharged, ct);
    }

    /** Bỏ qua (BR-27 RULE 2), không cần lý do. */
    public Task<FindingResponse> DismissAsync(Guid findingId, CancellationToken ct) =>
        DecideAsync(findingId, FindingStatus.Dismissed, null, AuditEventTypes.FindingDismissed, ct);

    /**
     * Mục tay (BR-27 RULE 3): ảnh nhận phòng thuộc biên bản nhận phòng của cùng hợp đồng, ảnh trả phòng thuộc
     * biên bản này, cả hai cùng khu vực. Tạo luôn ở trạng thái CHARGED.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<FindingResponse> AddManualAsync(Guid inspectionId, AddFindingRequest req, CancellationToken ct)
    {
        new Validator()
            .Require(req.AreaId is not null, "areaId", "Thiếu khu vực")
            .Text(req.Title, "title", 1, 80, "Tiêu đề")
            .Require(req.Description is null || req.Description.Trim().Length <= 500, "description", "Mô tả tối đa 500 ký tự")
            .Positive(req.Amount, "amount", "Số tiền")
            .Require(req.MoveInPhotoId is not null && req.MoveOutPhotoId is not null, "photos",
                "Mục tay phải gắn một cặp ảnh nhận phòng và trả phòng")
            .ThrowIfInvalid();
        var inspection = await LoadForLandlordAsync(inspectionId, ct);

        var moveInId = await db.Inspections
            .Where(i => i.TenancyId == inspection.TenancyId && i.Type == InspectionType.MoveIn)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        var matching = await db.Photos.CountAsync(p =>
            (p.Id == req.MoveInPhotoId && p.InspectionId == moveInId && p.RoomAreaId == req.AreaId)
            || (p.Id == req.MoveOutPhotoId && p.InspectionId == inspectionId && p.RoomAreaId == req.AreaId), ct);
        new Validator().Require(matching == 2, "photos", "Cặp ảnh phải là ảnh nhận phòng và ảnh trả phòng của cùng khu vực")
            .ThrowIfInvalid();

        var finding = new Finding
        {
            InspectionId = inspectionId,
            RoomAreaId = req.AreaId!.Value,
            MoveInPhotoId = req.MoveInPhotoId,
            MoveOutPhotoId = req.MoveOutPhotoId,
            Status = FindingStatus.Charged,
            Title = req.Title!.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            IsManual = true,
            Amount = req.Amount,
        };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockReviewingAsync(inspectionId, ct);
        await EnsureWithinDepositAsync(inspection, null, req.Amount!.Value, ct);
        db.Findings.Add(finding);
        db.Audit(AuditEventTypes.FindingAdded, nameof(Finding), finding.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToResponse(finding);
    }

    /**
     * Gửi người thuê (BR-27 RULE 4): REVIEWING → AWAITING_CONFIRM trước (giữ khóa dòng), rồi kiểm không còn SUGGESTED
     * (409 PENDING_FINDINGS, rollback). Đã gửi rồi → 200, không báo lại. Sau commit phát FindingsSent cho notify.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<SendFindingsResponse> SendToTenantAsync(Guid inspectionId, CancellationToken ct)
    {
        var inspection = await LoadInspectionAsync(inspectionId, ct);
        EnsureLandlord(inspection);
        long total;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.Inspections.Where(i => i.Id == inspectionId && i.Status == InspectionStatus.Reviewing)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InspectionStatus.AwaitingConfirm)
                    .SetProperty(i => i.UpdatedAt, clock.GetUtcNow()), ct);
            if (rows == 0)
            {
                var current = await db.Inspections.Where(i => i.Id == inspectionId).Select(i => i.Status).SingleAsync(ct);
                if (current == InspectionStatus.AwaitingConfirm)
                {
                    return new SendFindingsResponse(inspectionId, current, await ChargedTotalAsync(inspectionId, null, ct));
                }
                throw StateError(current);
            }

            var pending = await db.Findings.CountAsync(f => f.InspectionId == inspectionId && f.Status == FindingStatus.Suggested, ct);
            if (pending > 0)
            {
                throw new ConflictException(ErrorCodes.PendingFindings, $"Còn {pending} gợi ý chưa xử lý");
            }

            total = await ChargedTotalAsync(inspectionId, null, ct);
            db.Audit(AuditEventTypes.FindingsSent, nameof(Inspection), inspectionId, $$"""{"total":{{total}}}""");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        await events.PublishAsync("FindingsSent", new
        {
            inspectionId,
            tenancyId = inspection.TenancyId,
            notifyUserId = inspection.Tenancy!.TenantId,
            total,
        }, ct);
        return new SendFindingsResponse(inspectionId, InspectionStatus.AwaitingConfirm, total);
    }

    /**
     * Người thuê đồng ý (BR-28 RULE 1, RULE 3): CHARGED → ACCEPTED; mục đang khiếu nại mà chủ nhà đã trả lời
     * (CORRECTED/KEPT) → RESOLVED và đóng khiếu nại. Trường hợp khác → 409 INVALID_STATE.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<FindingResponse> AcceptAsync(Guid findingId, CancellationToken ct)
    {
        var (finding, _) = await LoadForTenantAsync(findingId, ct);
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await MoveAsync(findingId, FindingStatus.Charged, FindingStatus.Accepted, ct))
        {
            db.Audit(AuditEventTypes.FindingAccepted, nameof(Finding), findingId);
        }
        else
        {
            var closed = await db.Disputes
                .Where(d => d.TargetType == DisputeTargetType.Finding && d.TargetId == findingId
                    && d.Status == DisputeStatus.Open && d.Resolution != null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DisputeStatus.Resolved)
                    .SetProperty(d => d.ResolvedAt, now).SetProperty(d => d.UpdatedAt, now), ct);
            if (closed == 0 || !await MoveAsync(findingId, FindingStatus.Disputed, FindingStatus.Resolved, ct))
            {
                throw new InvalidStateException("Mục này không thể đồng ý ở trạng thái hiện tại");
            }
            db.Audit(AuditEventTypes.FindingResolved, nameof(Finding), findingId);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToResponse(await db.Findings.AsNoTracking().SingleAsync(f => f.Id == finding.Id, ct));
    }

    /**
     * Người thuê khiếu nại (BR-28 RULE 2): lý do ≥ 10 ký tự; CHARGED → DISPUTED, tạo dispute FINDING; mục khiếu nại
     * chưa bị trừ. Sau commit phát FindingDisputed để báo chủ nhà.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<FindingResponse> DisputeAsync(Guid findingId, DisputeFindingRequest req, CancellationToken ct)
    {
        new Validator().Text(req.Reason, "reason", 10, 1000, "Lý do khiếu nại").ThrowIfInvalid();
        var (finding, inspection) = await LoadForTenantAsync(findingId, ct);
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await MoveAsync(findingId, FindingStatus.Charged, FindingStatus.Disputed, ct))
            {
                throw new InvalidStateException("Chỉ khiếu nại được mục đang bị tính tiền");
            }
            db.Disputes.Add(new Dispute
            {
                TenancyId = inspection.TenancyId,
                TargetType = DisputeTargetType.Finding,
                TargetId = findingId,
                RaisedBy = user.UserId,
                Reason = req.Reason!.Trim(),
            });
            db.Audit(AuditEventTypes.FindingDisputed, nameof(Finding), findingId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        await events.PublishAsync("FindingDisputed", new
        {
            findingId,
            inspectionId = inspection.Id,
            tenancyId = inspection.TenancyId,
            notifyUserId = inspection.Tenancy!.LandlordId,
        }, ct);
        return ToResponse(await db.Findings.AsNoTracking().SingleAsync(f => f.Id == finding.Id, ct));
    }

    /**
     * Chủ nhà trả lời khiếu nại (BR-28 RULE 3): CORRECTED (số tiền mới, trong giới hạn cọc), KEPT (bắt buộc phản hồi)
     * hoặc REMOVED (bỏ mục → DISMISSED, khiếu nại đóng ngay). Mỗi khiếu nại trả lời một lần. Phát DisputeAnswered.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<FindingResponse> RespondAsync(Guid findingId, RespondFindingDisputeRequest req, CancellationToken ct)
    {
        var action = req.Action?.Trim().ToUpperInvariant();
        new Validator()
            .Require(action is "CORRECTED" or "KEPT" or "REMOVED", "action", "action phải là CORRECTED, KEPT hoặc REMOVED")
            .Require(action != "CORRECTED" || req.Amount is > 0, "amount", "Số tiền mới phải lớn hơn 0")
            .Require(action != "KEPT" || !string.IsNullOrWhiteSpace(req.Response), "response", "Giữ nguyên thì cần phản hồi")
            .Require(req.Response is null || req.Response.Trim().Length <= 1000, "response", "Phản hồi tối đa 1000 ký tự")
            .ThrowIfInvalid();
        var finding = await db.Findings.FirstOrDefaultAsync(f => f.Id == findingId, ct)
            ?? throw new NotFoundException("Không tìm thấy mục");
        var inspection = await LoadInspectionAsync(finding.InspectionId, ct);
        EnsureLandlord(inspection);
        EnsureAwaitingTenant(inspection);

        var now = clock.GetUtcNow();
        var response = string.IsNullOrWhiteSpace(req.Response) ? null : req.Response.Trim();
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var answered = await db.Disputes
                .Where(d => d.TargetType == DisputeTargetType.Finding && d.TargetId == findingId
                    && d.Status == DisputeStatus.Open && d.Resolution == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Resolution, action == "KEPT" ? DisputeResolution.Kept : DisputeResolution.Corrected)
                    .SetProperty(d => d.Response, response)
                    .SetProperty(d => d.Status, action == "REMOVED" ? DisputeStatus.Resolved : DisputeStatus.Open)
                    .SetProperty(d => d.ResolvedAt, action == "REMOVED" ? now : null)
                    .SetProperty(d => d.UpdatedAt, now), ct);
            if (answered == 0 || finding.Status != FindingStatus.Disputed)
            {
                throw new InvalidStateException("Mục này không có khiếu nại đang chờ trả lời");
            }

            if (action == "CORRECTED")
            {
                await EnsureWithinDepositAsync(inspection, findingId, req.Amount!.Value, ct);
                finding.Amount = req.Amount;
            }
            else if (action == "REMOVED")
            {
                finding.Status = FindingStatus.Dismissed;
                finding.Amount = null;
            }
            db.Audit(AuditEventTypes.FindingDisputeAnswered, nameof(Finding), findingId, $$"""{"action":"{{action}}"}""");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        await events.PublishAsync("DisputeAnswered", new
        {
            findingId,
            inspectionId = inspection.Id,
            tenancyId = inspection.TenancyId,
            action,
            notifyUserId = inspection.Tenancy!.TenantId,
        }, ct);
        return ToResponse(finding);
    }

    private async Task<(Finding Finding, Inspection Inspection)> LoadForTenantAsync(Guid findingId, CancellationToken ct)
    {
        var finding = await db.Findings.AsNoTracking().FirstOrDefaultAsync(f => f.Id == findingId, ct)
            ?? throw new NotFoundException("Không tìm thấy mục");
        var inspection = await LoadInspectionAsync(finding.InspectionId, ct);
        if (user.UserId != inspection.Tenancy!.TenantId)
        {
            throw new ForbiddenException("Chỉ người thuê mới đồng ý hoặc khiếu nại được");
        }
        EnsureAwaitingTenant(inspection);
        return (finding, inspection);
    }

    private static void EnsureAwaitingTenant(Inspection inspection)
    {
        if (inspection.Status == InspectionStatus.Settled)
        {
            throw new RecordLockedException("Đã quyết toán, không thay đổi được");
        }
        if (inspection.Status != InspectionStatus.AwaitingConfirm)
        {
            throw new InvalidStateException("Chủ nhà chưa gửi danh sách cho người thuê");
        }
    }

    /** Đổi trạng thái có điều kiện (chỉ khi đang ở from); true nếu đổi được. */
    private async Task<bool> MoveAsync(Guid findingId, FindingStatus from, FindingStatus to, CancellationToken ct) =>
        await db.Findings.Where(f => f.Id == findingId && f.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, to).SetProperty(f => f.UpdatedAt, clock.GetUtcNow()), ct) == 1;

    private async Task<FindingResponse> DecideAsync(Guid findingId, FindingStatus status, long? amount, string auditType, CancellationToken ct)
    {
        var finding = await db.Findings.FirstOrDefaultAsync(f => f.Id == findingId, ct)
            ?? throw new NotFoundException("Không tìm thấy mục");
        var inspection = await LoadForLandlordAsync(finding.InspectionId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockReviewingAsync(inspection.Id, ct);
        if (status == FindingStatus.Charged)
        {
            await EnsureWithinDepositAsync(inspection, finding.Id, amount!.Value, ct);
        }
        finding.Status = status;
        finding.Amount = amount;
        db.Audit(auditType, nameof(Finding), finding.Id, amount is null ? null : $$"""{"amount":{{amount}}}""");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToResponse(finding);
    }

    private async Task<Inspection> LoadInspectionAsync(Guid inspectionId, CancellationToken ct) =>
        await db.Inspections.AsNoTracking().Include(i => i.Tenancy).FirstOrDefaultAsync(i => i.Id == inspectionId, ct)
        ?? throw new NotFoundException("Không tìm thấy biên bản");

    private async Task<Inspection> LoadForLandlordAsync(Guid inspectionId, CancellationToken ct)
    {
        var inspection = await LoadInspectionAsync(inspectionId, ct);
        EnsureLandlord(inspection);
        if (inspection.Status != InspectionStatus.Reviewing)
        {
            throw StateError(inspection.Status);
        }
        return inspection;
    }

    private void EnsureLandlord(Inspection inspection)
    {
        if (user.UserId != inspection.Tenancy!.LandlordId)
        {
            throw new ForbiddenException("Chỉ chủ nhà mới duyệt được các mục trả phòng");
        }
    }

    /** Khóa dòng inspection trong transaction; trạng thái vừa đổi (đã gửi) → lỗi tương ứng. */
    private async Task LockReviewingAsync(Guid inspectionId, CancellationToken ct)
    {
        var rows = await db.Inspections.Where(i => i.Id == inspectionId && i.Status == InspectionStatus.Reviewing)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, clock.GetUtcNow()), ct);
        if (rows == 0)
        {
            throw StateError(await db.Inspections.Where(i => i.Id == inspectionId).Select(i => i.Status).SingleAsync(ct));
        }
    }

    private static AppException StateError(InspectionStatus status) =>
        status is InspectionStatus.AwaitingConfirm or InspectionStatus.Settled
            ? new RecordLockedException("Danh sách đã gửi người thuê, không sửa được")
            : new InvalidStateException("Biên bản trả phòng chưa ở bước duyệt");

    private async Task EnsureWithinDepositAsync(Inspection inspection, Guid? findingId, long amount, CancellationToken ct)
    {
        var deposit = await db.TenancyVersions
            .Where(v => v.TenancyId == inspection.TenancyId && v.Status == TenancyVersionStatus.Locked)
            .OrderByDescending(v => v.Number).Select(v => v.Deposit).FirstOrDefaultAsync(ct) ?? 0;
        // Mọi mục còn có thể bị trừ (đã tính, đã đồng ý, đang khiếu nại, đã xử lý); trước khi gửi chỉ có CHARGED
        var others = await db.Findings
            .Where(f => f.InspectionId == inspection.Id && f.Id != findingId && Deductible.Contains(f.Status))
            .SumAsync(f => f.Amount ?? 0, ct);
        if (others + amount > deposit)
        {
            throw new BadRequestException(ErrorCodes.AmountExceedsDeposit,
                $"Tổng tiền trừ {others + amount:N0} đ vượt tiền cọc {deposit:N0} đ");
        }
    }

    private Task<long> ChargedTotalAsync(Guid inspectionId, Guid? exceptFindingId, CancellationToken ct) =>
        db.Findings.Where(f => f.InspectionId == inspectionId && f.Status == FindingStatus.Charged && f.Id != exceptFindingId)
            .SumAsync(f => f.Amount ?? 0, ct);

    private static FindingResponse ToResponse(Finding f) => new(
        f.Id, f.RoomAreaId, f.MoveInPhotoId, f.MoveOutPhotoId, f.Status, f.Title, f.Description,
        f.BoundingBoxJson is null ? null : JsonSerializer.Deserialize<BoundingBoxResponse>(f.BoundingBoxJson, Json),
        f.Confidence, f.PhashDistance, f.IsManual, f.UsedFallback, f.Amount);
}
