using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng tenancy: hợp đồng thuê của một phòng. landlord_id chép từ phòng để lọc quyền
 * mà không phải join; tenant_id có sau khi người thuê nhận lời mời.
 * opening_electricity, opening_water: chỉ số đồng hồ lúc nhận phòng (BR-18 RULE 5), nhập trong biên bản
 * nhận phòng; hóa đơn kỳ đầu tiên dùng làm chỉ số cũ.
 *
 * @author Tuấn
 * @version 1.2
 */
public class Tenancy : AuditableEntity
{
    public Guid RoomId { get; set; }
    public Guid LandlordId { get; set; }
    public Guid? TenantId { get; set; }
    public TenancyStatus Status { get; set; } = TenancyStatus.Draft;
    public long? OpeningElectricity { get; set; }
    public long? OpeningWater { get; set; }
    public Room? Room { get; set; }
    public List<TenancyVersion> Versions { get; set; } = [];
}
