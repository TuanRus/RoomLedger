using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Abstractions;

/**
 * Người dùng của request hiện tại. userId lấy từ claim sub, vai lấy từ cognito:groups
 * trong JWT đã được Cognito authorizer kiểm. Không bao giờ lấy từ body hay query.
 *
 * @author Tuấn
 * @version 1.1
 */
public interface ICurrentUser
{
    /** true khi request có token hợp lệ. */
    bool IsAuthenticated { get; }

    /**
     * true chỉ với Lambda nền (persist-results, notify...) không gắn với người dùng:
     * khi đó global query filter theo chủ sở hữu được bỏ qua.
     */
    bool IsSystem { get; }

    /** Cognito sub. Ném 401 nếu chưa đăng nhập. */
    Guid UserId { get; }

    /** Vai cao nhất trong cognito:groups (admin, landlord, tenant); null nếu không có group. */
    UserRole? Role { get; }

    /** Claim email (chữ thường). */
    string? Email { get; }

    /** Claim email_verified. */
    bool EmailVerified { get; }

    /** Claim name, có thể rỗng. */
    string? FullName { get; }
}
