namespace RoomLedger.Api.Endpoints;

public static class DisputeEndpoints
{
    public static RouteGroupBuilder MapDisputeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/disputes");
        // TODO: thêm route
        return api;
    }
}
