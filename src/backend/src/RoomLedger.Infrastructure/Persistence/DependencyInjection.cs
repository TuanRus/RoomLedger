using Amazon.EventBridge;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RoomLedger.Core.Abstractions;
using RoomLedger.Infrastructure.Events;
using RoomLedger.Infrastructure.S3;

namespace RoomLedger.Infrastructure.Persistence;

/**
 * Đăng ký DbContext, S3, EventBridge cho API và các Lambda.
 *
 * @author Tuấn
 * @version 1.1
 */
public static class DependencyInjection
{
    /**
     * Kết nối RDS PostgreSQL qua TLS. Khi Database:UseIamAuth = true, mật khẩu là RDS IAM token tạo trong Lambda;
     * máy cá nhân dùng PostgreSQL trong Docker với mật khẩu trong connection string.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("RoomLedger")
            ?? throw new InvalidOperationException("Thiếu ConnectionStrings:RoomLedger");
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        if (config.GetValue<bool>("Database:UseIamAuth"))
        {
            // Region lấy từ cấu hình AWS:Region hoặc AWS_REGION (Lambda tự có); không đoán region mặc định.
            var region = config["AWS:Region"] ?? Environment.GetEnvironmentVariable("AWS_REGION")
                ?? throw new InvalidOperationException("Thiếu AWS:Region hoặc biến môi trường AWS_REGION (cần khi Database:UseIamAuth = true)");
            builder.UseRdsIamAuth(region);
        }

        var dataSource = builder.Build();
        services.AddSingleton(dataSource);
        services.TryAddTimeProvider();
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());
        return services;
    }

    /**
     * Đăng ký S3 và EventBridge.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IServiceCollection AddAwsServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.Configure<EventBusOptions>(config.GetSection("EventBus"));
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        services.AddSingleton<IAmazonEventBridge>(_ => new AmazonEventBridgeClient());
        services.AddSingleton<IStorage, S3Storage>();
        services.AddSingleton<IObjectStore, S3ObjectStore>();   // Sang: xóa ảnh trước khi khóa (MD-04-FE-03)
        services.AddScoped<IEventPublisher, EventBridgePublisher>();
        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
