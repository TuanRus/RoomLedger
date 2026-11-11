namespace RoomLedger.Core.Domain;

/**
 * Bảng room_area: khu vực cần chụp của phòng và số ảnh tối thiểu (1–6).
 *
 * @author Tuấn
 * @version 1.1
 */
public class RoomArea : AuditableEntity
{
    public Guid RoomId { get; set; }
    public string Name { get; set; } = "";
    public int MinPhotos { get; set; }
    public int SortOrder { get; set; }
}
