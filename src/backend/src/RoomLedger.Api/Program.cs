using RoomLedger.Api.Endpoints;
using RoomLedger.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Chạy được cả local (Kestrel) lẫn trên Lambda sau API Gateway REST
builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);
builder.Services.AddProblemDetails();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

var api = app.MapGroup("/api/v1");
api.MapPropertyEndpoints();
api.MapRoomEndpoints();
api.MapTenancyEndpoints();
api.MapInspectionEndpoints();
api.MapUploadEndpoints();
api.MapMeterEndpoints();
api.MapInvoiceEndpoints();
api.MapCheckoutEndpoints();
api.MapDisputeEndpoints();
api.MapAdminEndpoints();

app.MapGet("/health", () => Results.Ok(new { success = true, message = "ok" }));

app.Run();

public partial class Program;
