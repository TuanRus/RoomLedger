namespace RoomLedger.Core.Domain;

/**
 * Bảng property: nhà trọ của một chủ nhà, kèm giá điện nước mặc định (VND).
 *
 * @author Tuấn
 * @version 1.1
 */
public class Property : AuditableEntity
{
    public Guid LandlordId { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public long ElectricityRate { get; set; }
    public long WaterRate { get; set; }
    public long ServiceFee { get; set; }
    public List<Room> Rooms { get; set; } = [];
}
