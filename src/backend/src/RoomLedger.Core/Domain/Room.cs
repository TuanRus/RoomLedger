using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng room: phòng trong nhà trọ. Version là cột hệ thống xmin của PostgreSQL,
 * dùng làm concurrency token khi PATCH.
 *
 * @author Tuấn
 * @version 1.1
 */
public class Room : AuditableEntity
{
    public Guid PropertyId { get; set; }
    public Guid LandlordId { get; set; }
    public string Code { get; set; } = "";
    public long MonthlyRent { get; set; }
    public RoomStatus Status { get; set; } = RoomStatus.Vacant;
    public uint Version { get; set; }
    public Property? Property { get; set; }
    public List<RoomArea> Areas { get; set; } = [];
    public List<Tenancy> Tenancies { get; set; } = [];
}
