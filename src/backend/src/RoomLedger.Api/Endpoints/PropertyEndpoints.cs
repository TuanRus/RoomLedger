using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Endpoints;

/**
 * Route /properties (MD-02, MD-05 theo nhà trọ).
 *
 * @author Tuấn
 * @version 1.1
 */
public static class PropertyEndpoints
{
    /**
     * Đăng ký route /properties.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static RouteGroupBuilder MapPropertyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/properties").RequireAuthorization(Policies.Landlord);

        group.MapGet("", async (int? limit, string? cursor, PropertyAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ListAsync(limit, cursor, ct), "Lấy danh sách nhà trọ thành công"));

        group.MapPost("", async (CreatePropertyRequest req, PropertyAppService svc, CancellationToken ct) =>
            ApiResults.Created(await svc.CreateAsync(req, ct), "Tạo nhà trọ thành công"));

        group.MapGet("/{propertyId:guid}/rooms", async (Guid propertyId, string? status, int? limit, string? cursor,
                RoomAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ListAsync(propertyId, EnumQuery.Parse<RoomDisplayStatus>(status, "status"), limit, cursor, ct), "Lấy danh sách phòng thành công"));

        group.MapPost("/{propertyId:guid}/rooms", async (Guid propertyId, CreateRoomRequest req, RoomAppService svc, CancellationToken ct) =>
            ApiResults.Created(await svc.CreateAsync(propertyId, req, ct), "Tạo phòng thành công"));

        group.MapGet("/{propertyId:guid}/meter-readings", async (Guid propertyId, string? period, MeterAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ListAsync(propertyId, period, ct)));

        group.MapPost("/{propertyId:guid}/meter-readings:confirm-all", async (Guid propertyId, ConfirmAllReadingsRequest req,
                MeterAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ConfirmAllAsync(propertyId, req, ct), "Xác nhận chỉ số thành công"));

        group.MapPost("/{propertyId:guid}/invoices:generate", async (Guid propertyId, GenerateInvoicesRequest req,
                InvoiceAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GenerateAsync(propertyId, req, ct), "Lập hóa đơn thành công"));

        return api;
    }
}
