using System.Linq.Expressions;
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
 * Nghiệp vụ phòng (MD-02-FE-02 đến FE-04): tạo, sửa, xem chi tiết và danh sách phòng.
 *
 * @author Tuấn
 * @version 1.0
 */
public class RoomAppService(AppDbContext db, ICurrentUser user, PropertyAppService properties)
{
    /**
     * Tạo phòng và khu vực (BR-06). Trùng mã phòng bị unique index ux_room_property_id_code chặn.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<RoomResponse> CreateAsync(Guid propertyId, CreateRoomRequest req, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        var areas = RoomService.ValidateRoom(req.Code, req.MonthlyRent, ToInputs(req.Areas));
        var code = req.Code!.Trim();
        if (await db.Rooms.AnyAsync(r => r.PropertyId == property.Id && r.Code == code, ct))
        {
            throw RoomCodeExists();
        }

        var room = new Room
        {
            PropertyId = property.Id,
            LandlordId = property.LandlordId,
            Code = code,
            MonthlyRent = req.MonthlyRent!.Value,
            Status = RoomStatus.Vacant,
            Areas = areas.Select((a, i) => new RoomArea { Name = a.Name, MinPhotos = a.MinPhotos, SortOrder = i }).ToList(),
        };
        db.Rooms.Add(room);
        db.Audit(AuditEventTypes.RoomCreated, nameof(Room), room.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw RoomCodeExists();
        }

        await db.Entry(room).ReloadAsync(ct);
        return ToResponse(room, null, false, null);
    }

    /**
     * Sửa giá thuê và/hoặc khu vực (BR-07). Không đổi khu vực khi phòng đang có người thuê;
     * version (xmin) khác lúc client đọc thì trả VERSION_CONFLICT.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<RoomResponse> UpdateAsync(Guid roomId, UpdateRoomRequest req, CancellationToken ct)
    {
        if (req.Version is null)
        {
            throw ValidationException.For("version", "Thiếu version của phòng");
        }

        var room = await GetOwnedAsync(roomId, ct);
        db.Entry(room).Property(r => r.Version).OriginalValue = req.Version.Value;

        new Validator().Require(req.MonthlyRent is null or > 0, "monthlyRent", "Giá thuê phải lớn hơn 0").ThrowIfInvalid();
        if (req.MonthlyRent is not null)
        {
            // Giá mới chỉ áp dụng cho hợp đồng sau; hợp đồng đang chạy dùng giá trong phiên bản hợp đồng
            room.MonthlyRent = req.MonthlyRent.Value;
        }

        if (req.Areas is not null)
        {
            var current = await CurrentTenancyStatusAsync(room.Id, ct);
            if (RoomService.AreasLocked(current))
            {
                throw new ConflictException(ErrorCodes.RoomOccupied, "Không sửa được khu vực khi phòng đang có người thuê");
            }

            await ReplaceAreasAsync(room, req.Areas, ct);
        }

        // Luôn đánh dấu sửa phòng để xmin đổi và concurrency token được kiểm, kể cả khi chỉ đổi khu vực
        db.Entry(room).Property(r => r.UpdatedAt).IsModified = true;
        db.Audit(AuditEventTypes.RoomUpdated, nameof(Room), room.Id);
        await db.SaveChangesAsync(ct);
        var detail = await GetAsync(room.Id, ct);
        return new RoomResponse(detail.RoomId, detail.PropertyId, detail.Code, detail.MonthlyRent, detail.Status,
            detail.DisplayStatus, detail.CurrentTenancy?.TenantName, detail.Areas, detail.Version);
    }

    /**
     * Chi tiết phòng, khu vực và hợp đồng hiện tại.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<RoomDetailResponse> GetAsync(Guid roomId, CancellationToken ct)
    {
        var landlordId = user.UserId;
        var row = await db.Rooms.AsNoTracking()
            .Where(r => r.Id == roomId && r.LandlordId == landlordId)
            .Select(r => new
            {
                Room = r,
                PropertyName = r.Property!.Name,
                Areas = r.Areas.OrderBy(a => a.SortOrder).ToList(),
                Tenancy = r.Tenancies.Where(t => t.Status != TenancyStatus.Settled)
                    .Select(t => new { t.Id, t.Status, t.TenantId }).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Không tìm thấy phòng");

        var awaiting = row.Tenancy is not null && await MoveInAwaitingAsync(row.Tenancy.Id, ct);
        string? tenantName = null;
        if (row.Tenancy?.TenantId is { } tid && (await TenantNamesAsync([tid], ct)).TryGetValue(tid, out var name))
        {
            tenantName = name;
        }

        var room = row.Room;
        return new RoomDetailResponse(
            room.Id, room.PropertyId, row.PropertyName, room.Code, room.MonthlyRent, room.Status,
            RoomService.DisplayStatus(row.Tenancy?.Status, awaiting),
            row.Areas.Select(a => new AreaResponse(a.Id, a.Name, a.MinPhotos)).ToList(),
            room.Version,
            row.Tenancy is null ? null : new CurrentTenancyResponse(row.Tenancy.Id, row.Tenancy.Status, tenantName));
    }

    /**
     * Danh sách phòng của một nhà trọ, lọc theo trạng thái hiển thị, phân trang keyset (BR-08).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PagedResult<RoomResponse>> ListAsync(Guid propertyId, RoomDisplayStatus? status, int? limit, string? cursor, CancellationToken ct)
    {
        var property = await properties.GetOwnedAsync(propertyId, ct);
        var take = Paging.NormalizeLimit(limit);
        var query = db.Rooms.AsNoTracking().Where(r => r.PropertyId == property.Id);
        if (status is not null)
        {
            query = query.Where(DisplayStatusFilter(status.Value));
        }

        var rows = await query
            .Keyset(PageCursor.Decode(cursor), take)
            .Select(r => new
            {
                Room = r,
                Areas = r.Areas.OrderBy(a => a.SortOrder).ToList(),
                Tenancy = r.Tenancies.Where(t => t.Status != TenancyStatus.Settled)
                    .Select(t => new { t.Id, t.Status, t.TenantId }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var tenancyIds = rows.Where(r => r.Tenancy is not null).Select(r => r.Tenancy!.Id).ToList();
        var awaiting = await db.Inspections.AsNoTracking()
            .Where(i => tenancyIds.Contains(i.TenancyId) && i.Type == InspectionType.MoveIn && i.Status == InspectionStatus.AwaitingConfirm)
            .Select(i => i.TenancyId)
            .ToListAsync(ct);
        var names = await TenantNamesAsync(rows.Where(r => r.Tenancy?.TenantId != null).Select(r => r.Tenancy!.TenantId!.Value), ct);

        return Paging.ToPage(rows, take, r => new PageCursor(r.Room.CreatedAt, r.Room.Id), r => ToResponse(
            r.Room,
            r.Tenancy?.Status,
            r.Tenancy is not null && awaiting.Contains(r.Tenancy.Id),
            r.Tenancy?.TenantId is { } t && names.TryGetValue(t, out var n) ? n : null,
            r.Areas));
    }

    /**
     * Lấy phòng thuộc chủ nhà đang đăng nhập (tracking), không thuộc thì 404.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<Room> GetOwnedAsync(Guid roomId, CancellationToken ct)
    {
        var landlordId = user.UserId;
        return await db.Rooms.Include(r => r.Areas).FirstOrDefaultAsync(r => r.Id == roomId && r.LandlordId == landlordId, ct)
            ?? throw new NotFoundException("Không tìm thấy phòng");
    }

    /**
     * Tên người thuê theo id. Bảng app_user bị lọc theo chính người gọi, nên chủ nhà đọc tên qua
     * IgnoreQueryFilters và chỉ với id lấy từ hợp đồng mà chủ nhà đã thấy được.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<Dictionary<Guid, string>> TenantNamesAsync(IEnumerable<Guid> tenantIds, CancellationToken ct)
    {
        var ids = tenantIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.AppUsers.IgnoreQueryFilters().AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private async Task ReplaceAreasAsync(Room room, List<AreaRequest> requested, CancellationToken ct)
    {
        var inputs = RoomService.ValidateAreasOnly(ToInputs(requested));
        var keepIds = requested.Where(a => a.AreaId is not null).Select(a => a.AreaId!.Value).ToHashSet();
        var unknown = keepIds.Except(room.Areas.Select(a => a.Id)).ToList();
        if (unknown.Count > 0)
        {
            throw ValidationException.For("areas", "Khu vực không thuộc phòng này");
        }

        var removed = room.Areas.Where(a => !keepIds.Contains(a.Id)).ToList();
        var removedIds = removed.Select(a => a.Id).ToList();
        if (removedIds.Count > 0 && await db.Photos.AnyAsync(p => removedIds.Contains(p.RoomAreaId), ct))
        {
            throw new InvalidStateException("Khu vực đã có ảnh trong biên bản cũ, không xóa được");
        }

        db.RoomAreas.RemoveRange(removed);
        var byId = room.Areas.ToDictionary(a => a.Id);
        for (var i = 0; i < inputs.Count; i++)
        {
            var id = i < requested.Count ? requested[i].AreaId : null;
            if (id is not null && byId.TryGetValue(id.Value, out var existing))
            {
                existing.Name = inputs[i].Name;
                existing.MinPhotos = inputs[i].MinPhotos;
                existing.SortOrder = i;
            }
            else
            {
                db.RoomAreas.Add(new RoomArea { RoomId = room.Id, Name = inputs[i].Name, MinPhotos = inputs[i].MinPhotos, SortOrder = i });
            }
        }
    }

    private async Task<TenancyStatus?> CurrentTenancyStatusAsync(Guid roomId, CancellationToken ct) =>
        await db.Tenancies.Where(t => t.RoomId == roomId && t.Status != TenancyStatus.Settled)
            .Select(t => (TenancyStatus?)t.Status).FirstOrDefaultAsync(ct);

    private Task<bool> MoveInAwaitingAsync(Guid tenancyId, CancellationToken ct) =>
        db.Inspections.AnyAsync(i => i.TenancyId == tenancyId && i.Type == InspectionType.MoveIn && i.Status == InspectionStatus.AwaitingConfirm, ct);

    private Expression<Func<Room, bool>> DisplayStatusFilter(RoomDisplayStatus status)
    {
        var inspections = db.Inspections;
        return status switch
        {
            RoomDisplayStatus.Vacant => r => !r.Tenancies.Any(t =>
                t.Status == TenancyStatus.AwaitingTenant || t.Status == TenancyStatus.Active || t.Status == TenancyStatus.MovingOut),
            RoomDisplayStatus.AwaitingConfirm => r => r.Tenancies.Any(t => t.Status == TenancyStatus.AwaitingTenant
                || (t.Status == TenancyStatus.Active && inspections.Any(i =>
                    i.TenancyId == t.Id && i.Type == InspectionType.MoveIn && i.Status == InspectionStatus.AwaitingConfirm))),
            RoomDisplayStatus.Active => r => r.Tenancies.Any(t => t.Status == TenancyStatus.Active
                && !inspections.Any(i => i.TenancyId == t.Id && i.Type == InspectionType.MoveIn && i.Status == InspectionStatus.AwaitingConfirm)),
            _ => r => r.Tenancies.Any(t => t.Status == TenancyStatus.MovingOut),
        };
    }

    private static List<AreaInput> ToInputs(List<AreaRequest>? areas) =>
        (areas ?? []).Select(a => new AreaInput(a.Name ?? "", a.MinPhotos)).ToList();

    private static ConflictException RoomCodeExists() =>
        new(ErrorCodes.RoomCodeExists, "Mã phòng đã tồn tại trong nhà trọ này");

    private static RoomResponse ToResponse(Room r, TenancyStatus? tenancyStatus, bool awaiting, string? tenantName, IEnumerable<RoomArea>? areas = null) =>
        new(r.Id, r.PropertyId, r.Code, r.MonthlyRent, r.Status, RoomService.DisplayStatus(tenancyStatus, awaiting), tenantName,
            (areas ?? r.Areas.OrderBy(a => a.SortOrder)).Select(a => new AreaResponse(a.Id, a.Name, a.MinPhotos)).ToList(),
            r.Version);
}
