namespace RoomLedger.Api.Endpoints;

public static class InvoiceEndpoints
{
    public static RouteGroupBuilder MapInvoiceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/invoices");
        // TODO: thêm route
        return api;
    }
}
