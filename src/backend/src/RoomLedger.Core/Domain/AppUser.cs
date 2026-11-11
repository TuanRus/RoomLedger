using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng app_user: hồ sơ người dùng, id chính là Cognito sub.
 * Tạo bởi trigger PostConfirmation, hoặc tạo bù khi gọi GET /me lần đầu.
 *
 * @author Tuấn
 * @version 1.1
 */
public class AppUser : AuditableEntity
{
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; }
}
