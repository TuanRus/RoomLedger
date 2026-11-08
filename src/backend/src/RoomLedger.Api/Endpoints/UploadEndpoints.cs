using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * POST /api/v1/uploads: xin presigned PUT để tải ảnh thẳng lên S3 (BR-16 RULE 2).
 *
 * @author Sang
 * @version 1.0
 */
public static class UploadEndpoints
{
    public static RouteGroupBuilder MapUploadEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/uploads");
        group.MapPost("", async (CreateUploadRequest req, UploadAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.CreateAsync(req, ct), "Đã tạo link tải lên")).RequireAuthorization(Policies.LandlordOrTenant);
        return api;
    }
}
