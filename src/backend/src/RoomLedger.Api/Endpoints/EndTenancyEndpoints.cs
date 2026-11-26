using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Route kết thúc hợp đồng và chuyển phòng: /tenancies/{id}/end (chỉ chủ nhà).
 *
 * @author Tuấn
 * @version 1.0
 */
public static class EndTenancyEndpoints
{
    /**
     * Đăng ký route /tenancies/{tenancyId}/end.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static RouteGroupBuilder MapEndTenancyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tenancies/{tenancyId:guid}").RequireAuthorization(Policies.Landlord);

        group.MapPost("/end", async (Guid tenancyId, EndTenancyRequest req, EndTenancyAppService svc, CancellationToken ct) =>
        {
            var (response, created) = await svc.StartAsync(tenancyId, req, ct);
            return created
                ? ApiResults.Created(response, "Đã bắt đầu kết thúc hợp đồng")
                : ApiResults.Ok(response, "Đã cập nhật thông tin kết thúc hợp đồng");
        });

        group.MapGet("/end", async (Guid tenancyId, EndTenancyAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetAsync(tenancyId, ct)));

        group.MapPost("/end:finish", async (Guid tenancyId, FinishEndTenancyRequest req, EndTenancyAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.FinishAsync(tenancyId, req, ct), "Đã chốt kết thúc hợp đồng"));

        group.MapDelete("/end", async (Guid tenancyId, EndTenancyAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.CancelAsync(tenancyId, ct), "Đã hủy kết thúc hợp đồng"));

        return api;
    }
}
