namespace RoomLedger.Core.Domain;

/**
 * Bảng invite: link mời người thuê. Chỉ lưu SHA-256 của token, không lưu token gốc.
 * Lời mời gắn với một email (chữ thường), hạn 72 giờ, dùng một lần.
 *
 * @author Tuấn
 * @version 1.1
 */
public class Invite : AuditableEntity
{
    public Guid TenancyId { get; set; }
    public string TokenHash { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? UsedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Tenancy? Tenancy { get; set; }
}
