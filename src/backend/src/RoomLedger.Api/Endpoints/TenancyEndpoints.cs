namespace RoomLedger.Api.Endpoints;

public static class TenancyEndpoints
{
    public static RouteGroupBuilder MapTenancyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tenancies");
        // TODO: thêm route
        return api;
    }
}
