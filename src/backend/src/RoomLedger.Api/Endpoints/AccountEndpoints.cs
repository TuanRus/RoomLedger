using RoomLedger.Api.Auth;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Route MD-01: GET /me, lời mời, trang chủ người thuê.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class AccountEndpoints
{
    /**
     * Đăng ký route của MD-01.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (AccountAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetMeAsync(ct))).RequireAuthorization(Policies.AnyUser);

        // Người chưa đăng nhập mở link mời: không cần token
        api.MapGet("/invites/{token}", async (string token, AccountAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.PreviewInviteAsync(token, ct))).AllowAnonymous();

        api.MapPost("/invites/{token}/accept", async (string token, AccountAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.AcceptInviteAsync(token, ct), "Nhận lời mời thành công")).RequireAuthorization(Policies.Tenant);

        api.MapGet("/tenant/home", async (AccountAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetTenantHomeAsync(ct))).RequireAuthorization(Policies.Tenant);

        return api;
    }
}
