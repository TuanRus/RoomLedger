using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Route POST /uploads (presigned PUT kèm x-amz-checksum-sha256).
 *
 * @author Tuấn
 * @version 1.1
 */
public static class UploadEndpoints
{
    /**
     * Đăng ký route /uploads.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static RouteGroupBuilder MapUploadEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/uploads", async (CreateUploadRequest req, UploadAppService svc, CancellationToken ct) =>
            ApiResults.Created(await svc.CreateAsync(req, ct), "Tạo link tải lên thành công"))
            .RequireAuthorization(Policies.LandlordOrTenant);
        return api;
    }
}
