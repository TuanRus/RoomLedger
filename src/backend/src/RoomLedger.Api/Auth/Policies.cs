using Microsoft.AspNetCore.Authorization;

namespace RoomLedger.Api.Auth;

/**
 * Policy phân quyền theo group Cognito. Mọi endpoint (trừ /health và GET /invites/{token}) phải gắn một policy.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class Policies
{
    public const string LandlordGroup = "landlord";
    public const string TenantGroup = "tenant";
    public const string AdminGroup = "admin";

    public const string Landlord = "Landlord";
    public const string Tenant = "Tenant";
    public const string LandlordOrTenant = "LandlordOrTenant";
    public const string Admin = "Admin";
    public const string AnyUser = "AnyUser";

    /**
     * Đăng ký các policy theo group.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static AuthorizationBuilder AddRoomLedgerPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy(Landlord, p => p.RequireAssertion(c => InGroup(c, LandlordGroup)))
        .AddPolicy(Tenant, p => p.RequireAssertion(c => InGroup(c, TenantGroup)))
        .AddPolicy(LandlordOrTenant, p => p.RequireAssertion(c => InGroup(c, LandlordGroup) || InGroup(c, TenantGroup)))
        .AddPolicy(Admin, p => p.RequireAssertion(c => InGroup(c, AdminGroup)))
        .AddPolicy(AnyUser, p => p.RequireAuthenticatedUser());

    private static bool InGroup(AuthorizationHandlerContext context, string group) =>
        context.User.Identity?.IsAuthenticated == true
        && CurrentUser.ParseGroups(context.User.FindAll("cognito:groups").Select(c => c.Value)).Contains(group);
}
