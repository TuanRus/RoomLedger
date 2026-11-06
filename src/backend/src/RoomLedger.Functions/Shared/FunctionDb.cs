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
            builder.UseRdsIamAuth(Environment.GetEnvironmentVariable("AWS_REGION") ?? "ap-southeast-1");
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
