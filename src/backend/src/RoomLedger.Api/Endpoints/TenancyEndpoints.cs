using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Route /tenancies (MD-03).
 *
 * @author Tuấn
 * @version 1.1
 */
public static class TenancyEndpoints
{
    /**
     * Đăng ký route /tenancies.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static RouteGroupBuilder MapTenancyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tenancies");

        group.MapGet("/{tenancyId:guid}", async (Guid tenancyId, TenancyAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetAsync(tenancyId, ct))).RequireAuthorization(Policies.LandlordOrTenant);

        group.MapPost("/{tenancyId:guid}/versions", async (Guid tenancyId, CreateTenancyVersionRequest req, TenancyAppService svc, CancellationToken ct) =>
            ApiResults.Created(await svc.AddVersionAsync(tenancyId, req, ct), "Tạo phiên bản hợp đồng thành công"))
            .RequireAuthorization(Policies.Landlord);

        group.MapPatch("/{tenancyId:guid}/versions/{v:int}/terms", async (Guid tenancyId, int v, UpdateTermsRequest req, TenancyAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.UpdateTermsAsync(tenancyId, v, req, ct), "Lưu điều khoản thành công"))
            .RequireAuthorization(Policies.Landlord);

        group.MapPost("/{tenancyId:guid}/invites", async (Guid tenancyId, CreateInviteRequest req, TenancyAppService svc, CancellationToken ct) =>
            ApiResults.Created(await svc.CreateInviteAsync(tenancyId, req, ct), "Đã gửi lời mời"))
            .RequireAuthorization(Policies.Landlord);

        group.MapPost("/{tenancyId:guid}/versions/{v:int}/confirm", async (Guid tenancyId, int v, ConfirmTenancyVersionRequest req, TenancyAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ConfirmAsync(tenancyId, v, req, ct), "Đã ghi nhận xác nhận"))
            .RequireAuthorization(Policies.Tenant);

        return api;
    }
}
