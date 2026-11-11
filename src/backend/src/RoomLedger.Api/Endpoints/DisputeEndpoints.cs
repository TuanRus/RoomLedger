using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Route /disputes. Khiếu nại chỉ số (MD-05) do Tuấn làm; khiếu nại mục trừ cọc (MD-06) Sang bổ sung.
 *
 * @author Tuấn
 * @version 1.1
 */
public static class DisputeEndpoints
{
    /**
     * Đăng ký route /disputes.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static RouteGroupBuilder MapDisputeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/disputes").RequireAuthorization(Policies.Landlord);
        group.MapPatch("/{disputeId:guid}", async (Guid disputeId, ResolveDisputeRequest req, InvoiceAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ResolveDisputeAsync(disputeId, req, ct), "Đã xử lý khiếu nại"));
        return api;
    }
}
