using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng tenancy: hợp đồng thuê của một phòng. landlord_id chép từ phòng để lọc quyền
 * mà không phải join; tenant_id có sau khi người thuê nhận lời mời.
 *
 * @author Tuấn
 * @version 1.1
 */
public class Tenancy : AuditableEntity
{
    public Guid RoomId { get; set; }
    public Guid LandlordId { get; set; }
    public Guid? TenantId { get; set; }
    public TenancyStatus Status { get; set; } = TenancyStatus.Draft;
    public Room? Room { get; set; }
    public List<TenancyVersion> Versions { get; set; } = [];
}
