namespace RoomLedger.Core.Abstractions;

/**
 * Tìm email của người dùng theo id (Cognito sub, cũng là app_user.id) khi sự kiện chỉ mang id.
 *
 * @author Tuấn
 * @version 1.0
 */
public interface IUserEmailLookup
{
    /** Email đã xác thực của người dùng; null nếu không tìm thấy. */
    Task<string?> FindEmailAsync(Guid userId, CancellationToken ct);
}
