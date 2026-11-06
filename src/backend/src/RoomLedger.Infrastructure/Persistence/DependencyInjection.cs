using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace RoomLedger.Infrastructure.Persistence;

public static class DependencyInjection
{
    /// <summary>Kết nối RDS PostgreSQL qua TLS; mật khẩu là RDS IAM token tạo trong Lambda.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        return services;
    }
}
