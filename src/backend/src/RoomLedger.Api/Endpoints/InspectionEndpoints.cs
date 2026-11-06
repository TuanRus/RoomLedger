namespace RoomLedger.Api.Endpoints;

public static class InspectionEndpoints
{
    public static RouteGroupBuilder MapInspectionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/inspections");
        // TODO: thêm route
        return api;
    }
}
