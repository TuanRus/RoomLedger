namespace RoomLedger.Api.Endpoints;

public static class PropertyEndpoints
{
    public static RouteGroupBuilder MapPropertyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/properties");
        // TODO: thêm route
        return api;
    }
}
