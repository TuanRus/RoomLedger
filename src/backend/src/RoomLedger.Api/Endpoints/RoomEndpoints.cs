using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Route /rooms (MD-02, tạo hợp đồng MD-03, chỉ số MD-05).
 *
 * @author Tuấn
 * @version 1.1
 */
public static class RoomEndpoints
{
    /**
     * Đăng ký route /rooms.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static RouteGroupBuilder MapRoomEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/rooms");

        group.MapGet("/{roomId:guid}", async (Guid roomId, RoomAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetAsync(roomId, ct))).RequireAuthorization(Policies.Landlord);

        group.MapPatch("/{roomId:guid}", async (Guid roomId, UpdateRoomRequest req, RoomAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.UpdateAsync(roomId, req, ct), "Cập nhật phòng thành công")).RequireAuthorization(Policies.Landlord);

        group.MapPost("/{roomId:guid}/tenancies", async (Guid roomId, TenancyAppService svc, CancellationToken ct) =>
        {
            var (tenancy, created) = await svc.CreateAsync(roomId, ct);
            return created ? ApiResults.Created(tenancy, "Tạo hợp đồng nháp thành công") : ApiResults.Ok(tenancy, "Phòng đã có hợp đồng nháp");
        }).RequireAuthorization(Policies.Landlord);

        group.MapPost("/{roomId:guid}/meter-readings", async (Guid roomId, CreateMeterReadingRequest req, MeterAppService svc, CancellationToken ct) =>
        {
            var (reading, created) = await svc.CreateAsync(roomId, req, ct);
            return created ? ApiResults.Created(reading, "Tạo chỉ số thành công") : ApiResults.Ok(reading, "Đã tạo link tải lại ảnh đồng hồ");
        }).RequireAuthorization(Policies.LandlordOrTenant);

        return api;
    }
}
