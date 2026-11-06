namespace RoomLedger.Api.Endpoints;

public static class CheckoutEndpoints
{
    public static RouteGroupBuilder MapCheckoutEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/findings");
        // TODO: thêm route
        return api;
    }
}
