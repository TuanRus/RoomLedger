namespace RoomLedger.Api.Endpoints;

public static class UploadEndpoints
{
    public static RouteGroupBuilder MapUploadEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/uploads");
        // TODO: thêm route
        return api;
    }
}
