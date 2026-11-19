using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;

namespace RoomLedger.Api.OpenApi;

/**
 * Swagger UI và OpenAPI JSON sinh từ các endpoint, dùng để thử API khi chạy local.
 * Chỉ bật ở môi trường Development, hoặc khi đặt rõ OpenApi:Enabled=true. Production mặc định tắt.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class SwaggerSetup
{
    public const string ConfigKey = "OpenApi:Enabled";

    /**
     * Bật khi môi trường là Development; cấu hình OpenApi:Enabled (true/false) ghi đè nếu có.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static bool IsSwaggerEnabled(this WebApplicationBuilder builder) =>
        builder.Configuration.GetValue<bool?>(ConfigKey) ?? builder.Environment.IsDevelopment();

    /**
     * Đăng ký generator. Nút Authorize nhận ID token Cognito (Bearer) để gọi endpoint cần đăng nhập.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IServiceCollection AddRoomLedgerSwagger(this IServiceCollection services)
    {
        // Swashbuckle đọc JsonOptions của MVC để vẽ schema: đặt giống API thật (camelCase, enum UPPER_SNAKE_CASE)
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false));
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "RoomLedger API",
                Version = "v1",
                Description = "Sinh tự động từ code để test nhanh. Hợp đồng chính thức với FE vẫn là src/contracts/openapi.yaml.",
            });
            var bearer = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Dán ID token Cognito (không cần chữ Bearer).",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            o.AddSecurityDefinition("Bearer", bearer);
            o.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = Array.Empty<string>() });
        });
        return services;
    }

    /**
     * Phát OpenAPI JSON tại /swagger/v1/swagger.json và giao diện tại /swagger.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static WebApplication UseRoomLedgerSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "RoomLedger API v1");
            o.DocumentTitle = "RoomLedger API";
        });
        return app;
    }
}
