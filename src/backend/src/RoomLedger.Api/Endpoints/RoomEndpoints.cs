namespace RoomLedger.Api.Endpoints;

public static class RoomEndpoints
{
    public static RouteGroupBuilder MapRoomEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/rooms");
        // TODO: thêm route
        return api;
    }
}
