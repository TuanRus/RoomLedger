namespace RoomLedger.Core.Domain.Enums;

/**
 * Vai của người dùng, lấy từ group Cognito.
 * Lưu trong database và trả ra API dạng UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.0
 */
public enum UserRole
{
    Landlord,
    Tenant,
    Admin,
}
