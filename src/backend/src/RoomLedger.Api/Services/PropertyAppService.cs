using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Contracts;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Services;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Services;

/**
 * Nghiệp vụ nhà trọ (MD-02-FE-01): tạo và liệt kê nhà trọ của chủ nhà.
 *
 * @author Tuấn
 * @version 1.0
 */
public class PropertyAppService(AppDbContext db, ICurrentUser user)
{
    /**
     * Tạo nhà trọ (BR-05). landlordId lấy từ claim sub, không nhận từ body.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PropertyResponse> CreateAsync(CreatePropertyRequest req, CancellationToken ct)
    {
        RoomService.ValidateProperty(req.Name, req.Address, req.ElectricityRate, req.WaterRate, req.ServiceFee);
        var property = new Property
        {
            LandlordId = user.UserId,
            Name = req.Name!.Trim(),
            Address = req.Address!.Trim(),
            ElectricityRate = req.ElectricityRate!.Value,
            WaterRate = req.WaterRate!.Value,
            ServiceFee = req.ServiceFee ?? 0,
        };
        db.Properties.Add(property);
        db.Audit(AuditEventTypes.PropertyCreated, nameof(Property), property.Id);
        await db.SaveChangesAsync(ct);
        return ToResponse(property, 0);
    }

    /**
     * Danh sách nhà trọ của chủ nhà, mới nhất trước, phân trang keyset.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<PagedResult<PropertyResponse>> ListAsync(int? limit, string? cursor, CancellationToken ct)
    {
        var take = Paging.NormalizeLimit(limit);
        var landlordId = user.UserId;
        var rows = await db.Properties.AsNoTracking()
            .Where(p => p.LandlordId == landlordId)
            .Keyset(PageCursor.Decode(cursor), take)
            .Select(p => new { Property = p, RoomCount = p.Rooms.Count })
            .ToListAsync(ct);
        return Paging.ToPage(rows, take, r => new PageCursor(r.Property.CreatedAt, r.Property.Id), r => ToResponse(r.Property, r.RoomCount));
    }

    /**
     * Lấy nhà trọ thuộc chủ nhà đang đăng nhập; không thuộc về mình thì 404 (không lộ dữ liệu người khác).
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<Property> GetOwnedAsync(Guid propertyId, CancellationToken ct)
    {
        var landlordId = user.UserId;
        return await db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId && p.LandlordId == landlordId, ct)
            ?? throw new NotFoundException("Không tìm thấy nhà trọ");
    }

    private static PropertyResponse ToResponse(Property p, int roomCount) =>
        new(p.Id, p.Name, p.Address, p.ElectricityRate, p.WaterRate, p.ServiceFee, roomCount, p.CreatedAt);
}
