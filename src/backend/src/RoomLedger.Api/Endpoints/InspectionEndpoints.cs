using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;

namespace RoomLedger.Api.Endpoints;

/**
 * Biên bản nhận phòng (MD-04). Chủ nhà hoặc người thuê của hợp đồng.
 *
 * @author Sang
 * @version 1.0
 */
public static class InspectionEndpoints
{
    public static RouteGroupBuilder MapInspectionEndpoints(this RouteGroupBuilder api)
    {
        // MD-04-FE-01: tạo mới → 201, hợp đồng đã có biên bản nhận phòng → 200 cùng biên bản đó
        api.MapPost("/tenancies/{tenancyId:guid}/inspections/move-in", async (Guid tenancyId, InspectionAppService svc, CancellationToken ct) =>
        {
            var (inspection, created) = await svc.OpenMoveInAsync(tenancyId, ct);
            return created
                ? ApiResults.Created(inspection, "Đã mở biên bản nhận phòng")
                : ApiResults.Ok(inspection, "Hợp đồng đã có biên bản nhận phòng");
        }).RequireAuthorization(Policies.LandlordOrTenant);

        // MD-06-FE-01: người thuê mở biên bản trả phòng; tạo mới → 201, đã có → 200
        api.MapPost("/tenancies/{tenancyId:guid}/inspections/move-out", async (Guid tenancyId, InspectionAppService svc, CancellationToken ct) =>
        {
            var (inspection, created) = await svc.OpenMoveOutAsync(tenancyId, ct);
            return created
                ? ApiResults.Created(inspection, "Đã mở biên bản trả phòng")
                : ApiResults.Ok(inspection, "Hợp đồng đã có biên bản trả phòng");
        }).RequireAuthorization(Policies.LandlordOrTenant);

        var group = api.MapGroup("/inspections");
        group.MapGet("/{inspectionId:guid}", async (Guid inspectionId, InspectionAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetAsync(inspectionId, ct))).RequireAuthorization(Policies.LandlordOrTenant);

        // Danh sách ảnh kèm URL xem có hạn (màn hình biên bản, ảnh đối chiếu khi trả phòng)
        group.MapGet("/{inspectionId:guid}/photos", async (Guid inspectionId, InspectionAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ListPhotosAsync(inspectionId, ct))).RequireAuthorization(Policies.LandlordOrTenant);

        // MD-06-FE-01 RULE 4: người thuê gửi biên bản trả phòng để so sánh
        group.MapPost("/{inspectionId:guid}/submit", async (Guid inspectionId, InspectionAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.SubmitMoveOutAsync(inspectionId, ct), "Đã gửi biên bản để so sánh"))
            .RequireAuthorization(Policies.LandlordOrTenant);

        // MD-04-FE-04: xác nhận; đủ hai bên thì khóa
        group.MapPost("/{inspectionId:guid}/confirm", async (Guid inspectionId, ConfirmInspectionRequest req, InspectionAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ConfirmAsync(inspectionId, req, ct), "Đã ghi nhận xác nhận"))
            .RequireAuthorization(Policies.LandlordOrTenant);

        // MD-04-FE-03: xóa ảnh trước khi khóa; trả lại biên bản để FE cập nhật số ảnh
        api.MapDelete("/photos/{photoId:guid}", async (Guid photoId, InspectionAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.DeletePhotoAsync(photoId, ct), "Đã xóa ảnh")).RequireAuthorization(Policies.LandlordOrTenant);
        return api;
    }
}
