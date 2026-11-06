namespace RoomLedger.Api.Endpoints;

public static class MeterEndpoints
{
    public static RouteGroupBuilder MapMeterEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/meter-readings");
        // TODO: thêm route
        return api;
    }
}
