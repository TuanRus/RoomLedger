using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Xem trước lời mời cho người chưa đăng nhập (email đã che một phần).
 *
 * @author Tuấn
 * @version 1.0
 */
public record InvitePreviewResponse(string PropertyName, string RoomCode, string EmailMasked, DateTimeOffset ExpiresAt);

/**
 * Kết quả nhận lời mời.
 *
 * @author Tuấn
 * @version 1.0
 */
public record AcceptInviteResponse(Guid TenancyId, TenancyStatus Status);

/**
 * Hồ sơ người dùng hiện tại (GET /me).
 *
 * @author Tuấn
 * @version 1.0
 */
public record MeResponse(Guid UserId, string FullName, string? Email, UserRole? Role);
