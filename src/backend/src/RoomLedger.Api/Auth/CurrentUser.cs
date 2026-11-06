using System.Security.Claims;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Auth;

/**
 * Đọc sub, email, email_verified và cognito:groups từ token đã được Cognito authorizer của API Gateway kiểm.
 * Trên Lambda, Amazon.Lambda.AspNetCoreServer chép claim của authorizer vào HttpContext.User;
 * chạy local thì JwtBearer kiểm token của User Pool dev.
 *
 * @author Tuấn
 * @version 1.1
 */
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && Sub is not null;

    public bool IsSystem => false;

    public Guid UserId => Sub ?? throw new AppException(401, ErrorCodes.Unauthorized, "Bạn cần đăng nhập");

    public UserRole? Role => ResolveRole(Groups);

    public string? Email => Find("email")?.Trim().ToLowerInvariant();

    public bool EmailVerified => string.Equals(Find("email_verified"), "true", StringComparison.OrdinalIgnoreCase);

    public string? FullName => Find("name");

    /** Các group trong claim cognito:groups. */
    public IReadOnlyList<string> Groups => ParseGroups(Principal?.FindAll("cognito:groups").Select(c => c.Value) ?? []);

    private Guid? Sub => Guid.TryParse(Find("sub") ?? Find(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private string? Find(string type) => Principal?.FindFirst(type)?.Value;

    /**
     * cognito:groups có thể đến dạng nhiều claim, chuỗi "[admin, landlord]" (REST API authorizer)
     * hoặc "admin,landlord". Gom về danh sách tên group chữ thường.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IReadOnlyList<string> ParseGroups(IEnumerable<string> values) =>
        values
            .SelectMany(v => v.Trim('[', ']', ' ').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(g => g.Trim('"', ' ').ToLowerInvariant())
            .Where(g => g.Length > 0)
            .Distinct()
            .ToList();

    /**
     * Một người có thể ở nhiều group; chọn vai cao nhất admin &gt; landlord &gt; tenant.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static UserRole? ResolveRole(IReadOnlyList<string> groups) =>
        groups.Contains(Policies.AdminGroup) ? UserRole.Admin
        : groups.Contains(Policies.LandlordGroup) ? UserRole.Landlord
        : groups.Contains(Policies.TenantGroup) ? UserRole.Tenant
        : null;
}
