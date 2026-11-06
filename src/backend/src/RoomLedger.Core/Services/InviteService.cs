using System.Security.Cryptography;
using System.Text;
using RoomLedger.Core.Common;

namespace RoomLedger.Core.Services;

/**
 * Token mời: token gốc chỉ đi trong email, database chỉ giữ hash.
 *
 * @author Tuấn
 * @version 1.0
 */
public record InviteToken(string Token, string TokenHash);

/**
 * Luật của link mời người thuê (BR-04, BR-12).
 *
 * @author Tuấn
 * @version 1.0
 */
public static class InviteService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    /**
     * Sinh token ngẫu nhiên 32 byte (RandomNumberGenerator), mã hóa base64url để đặt vào link.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static InviteToken GenerateToken()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new InviteToken(token, HashToken(token));
    }

    /**
     * SHA-256 của token, base64 (44 ký tự) để so với cột invite.token_hash.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /**
     * Chuẩn hóa email để so sánh không phân biệt hoa thường; sai định dạng ném INVALID_EMAIL.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string NormalizeEmail(string? email)
    {
        if (!Validator.IsValidEmail(email))
        {
            throw new BadRequestException(ErrorCodes.InvalidEmail, "Email không đúng định dạng");
        }

        return email!.Trim().ToLowerInvariant();
    }

    /**
     * Che một phần email để hiện ở màn xem trước lời mời (người chưa đăng nhập cũng xem được).
     * Ví dụ minh.tran@gmail.com → m*******n@gmail.com.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        var local = email[..at];
        var masked = local.Length <= 2
            ? local[0] + new string('*', Math.Max(1, local.Length - 1))
            : local[0] + new string('*', local.Length - 2) + local[^1];
        return masked + email[at..];
    }

    /**
     * Lời mời còn dùng được: chưa dùng, chưa thu hồi, chưa hết hạn.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static bool IsUsable(DateTimeOffset? usedAt, DateTimeOffset? revokedAt, DateTimeOffset expiresAt, DateTimeOffset now) =>
        usedAt is null && revokedAt is null && expiresAt > now;

    /**
     * Email đã xác minh trong token phải trùng email được mời (BR-04 RULE 3).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void EnsureEmailMatches(string? tokenEmail, bool emailVerified, string inviteEmail)
    {
        if (!emailVerified || tokenEmail is null
            || !string.Equals(tokenEmail.Trim(), inviteEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Link mời này dành cho email khác", ErrorCodes.InviteEmailMismatch);
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
