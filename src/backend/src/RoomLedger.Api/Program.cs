using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using RoomLedger.Api.Auth;
using RoomLedger.Api.Endpoints;
using RoomLedger.Api.Middleware;
using RoomLedger.Api.Services;
using RoomLedger.Core.Abstractions;
using RoomLedger.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Chạy được cả local (Kestrel) lẫn trên Lambda sau API Gateway REST
builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

// JSON: tên trường camelCase, enum dạng chuỗi UPPER_SNAKE_CASE (AWAITING_CONFIRM)
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
});

// Trên Lambda, Cognito authorizer của API Gateway đã kiểm token và chép claim vào HttpContext.User.
// Chạy local thì cấu hình Cognito:Authority (User Pool dev) để JwtBearer tự kiểm ID token.
// Không đặt scheme mặc định trên Lambda để giữ nguyên HttpContext.User do API Gateway truyền vào.
var authority = builder.Configuration["Cognito:Authority"];
var authentication = builder.Services.AddAuthentication();
if (!string.IsNullOrEmpty(authority))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
    authentication.AddJwtBearer(o =>
        {
            o.Authority = authority;
            o.MapInboundClaims = false;
            o.TokenValidationParameters.ValidateAudience = !string.IsNullOrEmpty(builder.Configuration["Cognito:ClientId"]);
            o.TokenValidationParameters.ValidAudience = builder.Configuration["Cognito:ClientId"];
        });
}

// Body JSON hỏng thì ném BadHttpRequestException để ErrorHandlingMiddleware trả VALIDATION_ERROR chuẩn
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

builder.Services.AddAuthorizationBuilder().AddRoomLedgerPolicies();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, JsonAuthorizationResultHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddAwsServices(builder.Configuration);
builder.Services.AddScoped<PropertyAppService>();
builder.Services.AddScoped<RoomAppService>();
builder.Services.AddScoped<TenancyAppService>();
builder.Services.AddScoped<AccountAppService>();
builder.Services.AddScoped<UploadAppService>();
builder.Services.AddScoped<MeterAppService>();
builder.Services.AddScoped<InvoiceAppService>();

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api/v1");
api.MapAccountEndpoints();
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

api.MapGet("/health", () => Results.Ok(new { success = true, message = "ok" })).AllowAnonymous();

// Route không tồn tại cũng trả định dạng lỗi chuẩn
app.MapFallback((HttpContext ctx) => ErrorHandlingMiddleware.WriteErrorAsync(ctx, 404, RoomLedger.Core.Common.ErrorCodes.NotFound, "Không tìm thấy đường dẫn"));

app.Run();

public partial class Program;
