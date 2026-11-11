using RoomLedger.Api.Auth;
using RoomLedger.Api.Contracts;
using RoomLedger.Api.Services;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Endpoints;

/**
 * Route /invoices (MD-05).
 *
 * @author Tuấn
 * @version 1.1
 */
public static class InvoiceEndpoints
{
    /**
     * Đăng ký route /invoices.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static RouteGroupBuilder MapInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/invoices");

        group.MapGet("", async (string? period, string? status, int? limit, string? cursor, InvoiceAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.ListAsync(period, EnumQuery.Parse<InvoiceStatus>(status, "status"), limit, cursor, ct), "Lấy danh sách hóa đơn thành công"))
            .RequireAuthorization(Policies.LandlordOrTenant);

        group.MapGet("/{invoiceId:guid}", async (Guid invoiceId, InvoiceAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.GetAsync(invoiceId, ct))).RequireAuthorization(Policies.LandlordOrTenant);

        group.MapPost("/{invoiceId:guid}/send", async (Guid invoiceId, InvoiceAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.SendAsync(invoiceId, ct), "Đã gửi hóa đơn")).RequireAuthorization(Policies.Landlord);

        group.MapPost("/{invoiceId:guid}/disputes", async (Guid invoiceId, CreateDisputeRequest req, InvoiceAppService svc, CancellationToken ct) =>
            ApiResults.Created(await svc.CreateDisputeAsync(invoiceId, req, ct), "Đã gửi khiếu nại")).RequireAuthorization(Policies.Tenant);

        group.MapPost("/{invoiceId:guid}/paid", async (Guid invoiceId, InvoiceAppService svc, CancellationToken ct) =>
            ApiResults.Ok(await svc.MarkPaidAsync(invoiceId, ct), "Đã ghi nhận thanh toán")).RequireAuthorization(Policies.Landlord);

        return api;
    }
}
