using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng tenancy: hợp đồng thuê của một phòng. landlord_id chép từ phòng để lọc quyền
 * mà không phải join; tenant_id có sau khi người thuê nhận lời mời.
 * opening_electricity, opening_water: chỉ số đồng hồ lúc nhận phòng (BR-18 RULE 5), nhập trong biên bản
 * nhận phòng; hóa đơn kỳ đầu tiên dùng làm chỉ số cũ.
 * end_date, end_kind, move_target_room_id: ngày, hình thức và phòng chuyển đến khi chủ nhà kết thúc hợp đồng;
 * last_rent, ended_at, next_tenancy_id: tiền phòng kỳ cuối, thời điểm chốt và hợp đồng mới khi chuyển phòng (luồng kết thúc hợp đồng).
 *
 * @author Tuấn
 * @version 1.3
 */
public class Tenancy : AuditableEntity
{
    public Guid RoomId { get; set; }
    public Guid LandlordId { get; set; }
    public Guid? TenantId { get; set; }
    public TenancyStatus Status { get; set; } = TenancyStatus.Draft;
    public long? OpeningElectricity { get; set; }
    public long? OpeningWater { get; set; }
    public DateOnly? EndDate { get; set; }
    public EndKind? EndKind { get; set; }
    public Guid? MoveTargetRoomId { get; set; }
    public long? LastRent { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    /** Hợp đồng mới ở phòng đích khi kết thúc theo hình thức MOVE. */
    public Guid? NextTenancyId { get; set; }
    public Room? Room { get; set; }
    public List<TenancyVersion> Versions { get; set; } = [];
}
