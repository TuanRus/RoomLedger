using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Route /meter-readings (MD-05).
 *
 * @author Tuấn
 * @version 1.1
 */
public static class MeterEndpoints
{
    /**
     * Đăng ký route /meter-readings.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static RouteGroupBuilder MapMeterEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/meter-readings").RequireAuthorization(Policies.Landlord);
        group.MapPatch("/{readingId:guid}", async (Guid readingId, UpdateMeterReadingRequest req, MeterAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.UpdateAsync(readingId, req, ct), "Cập nhật chỉ số thành công"));
        return api;
    }
}
