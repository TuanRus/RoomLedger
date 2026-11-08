using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Core.StateMachines;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Biên bản nhận phòng phía API (MD-04). Chủ nhà hoặc người thuê của hợp đồng mới thấy biên bản
 * (global query filter); người khác nhận 404.
 *
 * @author Sang
 * @version 1.0
 */
public class InspectionAppService(AppDbContext db)
{
    private const string OneMoveInIndex = "ux_inspection_tenancy_id_type";

    /**
     * Mở biên bản nhận phòng (MD-04-FE-01, BR-15). Đã có thì trả biên bản đó (created = false).
     * Hai bên mở cùng lúc: unique index (tenancy_id, type) chặn bản thứ hai, request thua trả biên bản của bên thắng.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<(InspectionResponse Inspection, bool Created)> OpenMoveInAsync(Guid tenancyId, CancellationToken ct)
    {
        var tenancy = await db.Tenancies.FirstOrDefaultAsync(t => t.Id == tenancyId, ct)
            ?? throw new NotFoundException("Không tìm thấy hợp đồng");
        InspectionStateMachine.EnsureCanOpenMoveIn(tenancy);

        if (await FindMoveInIdAsync(tenancyId, ct) is { } existingId)
        {
            return (await GetAsync(existingId, ct), false);
        }

        var areas = await db.RoomAreas.Where(a => a.RoomId == tenancy.RoomId).ToListAsync(ct);
        var inspection = new Inspection
        {
            TenancyId = tenancy.Id,
            Type = InspectionType.MoveIn,
            Status = InspectionStatus.Open,
            AreaSnapshotJson = InspectionService.SerializeSnapshot(InspectionService.BuildAreaSnapshot(areas)),
        };
        db.Inspections.Add(inspection);
        db.Audit(AuditEventTypes.InspectionOpened, nameof(Inspection), inspection.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
                                          { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OneMoveInIndex })
        {
            db.ChangeTracker.Clear();
            var winner = await FindMoveInIdAsync(tenancyId, ct);
            if (winner is null)
            {
                throw;
            }
            return (await GetAsync(winner.Value, ct), false);
        }

        return (await GetAsync(inspection.Id, ct), true);
    }

    /**
     * Xem biên bản: khu vực theo bản sao lúc mở, kèm số ảnh đã gửi mỗi khu vực.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task<InspectionResponse> GetAsync(Guid inspectionId, CancellationToken ct)
    {
        var inspection = await db.Inspections.AsNoTracking().FirstOrDefaultAsync(i => i.Id == inspectionId, ct)
            ?? throw new NotFoundException("Không tìm thấy biên bản");
        var counts = await db.Photos.Where(p => p.InspectionId == inspection.Id)
            .GroupBy(p => p.RoomAreaId)
            .Select(g => new { AreaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AreaId, x => x.Count, ct);
        var areas = InspectionService.ParseSnapshot(inspection.AreaSnapshotJson)
            .Select(a => new InspectionAreaResponse(a.RoomAreaId, a.Name, a.MinPhotos, a.SortOrder, counts.GetValueOrDefault(a.RoomAreaId)))
            .ToList();
        return new InspectionResponse(inspection.Id, inspection.TenancyId, inspection.Type, inspection.Status,
            inspection.LandlordConfirmedAt, inspection.TenantConfirmedAt, areas);
    }

    private async Task<Guid?> FindMoveInIdAsync(Guid tenancyId, CancellationToken ct) =>
        await db.Inspections.Where(i => i.TenancyId == tenancyId && i.Type == InspectionType.MoveIn)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
}
