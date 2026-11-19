using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.Shared;

/**
 * Tạo AppDbContext cho Lambda nền: kết nối RDS bằng IAM auth (user roomledger_app), không bị query filter.
 * Data source được giữ lại giữa các lần gọi (warm start) để tái dùng connection pool.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class FunctionDb
{
    private static readonly Lazy<NpgsqlDataSource> DataSource = new(() =>
    {
        var cs = Environment.GetEnvironmentVariable("ROOMLEDGER_DB")
            ?? throw new InvalidOperationException("Thiếu biến môi trường ROOMLEDGER_DB");
        var builder = new NpgsqlDataSourceBuilder(cs);
        if (!cs.Contains("Password=", StringComparison.OrdinalIgnoreCase))
        {
            // Lambda luôn có AWS_REGION; không đoán region để tránh ký token IAM cho nhầm region.
            var region = Environment.GetEnvironmentVariable("AWS_REGION")
                ?? throw new InvalidOperationException("Thiếu biến môi trường AWS_REGION (cần cho RDS IAM auth)");
            builder.UseRdsIamAuth(region);
        }

        return builder.Build();
    });

    /**
     * Mở một DbContext hệ thống.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(DataSource.Value)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, SystemCurrentUser.Instance, TimeProvider.System);
    }
}
