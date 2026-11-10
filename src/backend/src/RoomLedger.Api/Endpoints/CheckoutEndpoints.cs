using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Trả phòng: duyệt các mục so sánh (MD-06-FE-03). Hai bên của hợp đồng; quyền chi tiết kiểm trong FindingAppService.
 *
 * @author Sang
 * @version 1.0
 */
public static class CheckoutEndpoints
{
    public static RouteGroupBuilder MapCheckoutEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/inspections/{inspectionId:guid}/findings", async (Guid inspectionId, FindingAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ListAsync(inspectionId, ct))).RequireAuthorization(Policies.LandlordOrTenant);

        // BR-27 RULE 3: mục tay của chủ nhà
        api.MapPost("/inspections/{inspectionId:guid}/findings", async (Guid inspectionId, AddFindingRequest req, FindingAppService svc, CancellationToken ct) =>
            ApiResults.Created(await svc.AddManualAsync(inspectionId, req, ct), "Đã thêm mục")).RequireAuthorization(Policies.LandlordOrTenant);

        // BR-27 RULE 4: gửi danh sách cho người thuê
        api.MapPost("/inspections/{inspectionId:guid}/send-to-tenant", async (Guid inspectionId, FindingAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.SendToTenantAsync(inspectionId, ct), "Đã gửi cho người thuê")).RequireAuthorization(Policies.LandlordOrTenant);

        var group = api.MapGroup("/findings");
        group.MapPost("/{findingId:guid}/charge", async (Guid findingId, ChargeFindingRequest req, FindingAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ChargeAsync(findingId, req, ct), "Đã tính tiền")).RequireAuthorization(Policies.LandlordOrTenant);
        group.MapPost("/{findingId:guid}/dismiss", async (Guid findingId, FindingAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.DismissAsync(findingId, ct), "Đã bỏ qua")).RequireAuthorization(Policies.LandlordOrTenant);
        return api;
    }
}
