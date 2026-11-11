using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Infrastructure.Persistence;

/**
 * Cho lệnh dotnet ef (migrations add, migrations script) tạo DbContext mà không cần chạy API.
 * Connection string lấy từ biến ROOMLEDGER_DB, mặc định là PostgreSQL trong docker compose.
 *
 * @author Tuấn
 * @version 1.0
 */
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ROOMLEDGER_DB")
            ?? "Host=localhost;Port=5432;Database=roomledger;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(options, SystemCurrentUser.Instance, TimeProvider.System);
    }
}

/**
 * Người dùng hệ thống cho Lambda nền và công cụ migration: không bị global query filter lọc.
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed class SystemCurrentUser : ICurrentUser
{
    public static readonly SystemCurrentUser Instance = new();

    public bool IsAuthenticated => false;
    public bool IsSystem => true;
    public Guid UserId => throw new InvalidOperationException("Lambda hệ thống không có userId");
    public UserRole? Role => null;
    public string? Email => null;
    public bool EmailVerified => false;
    public string? FullName => null;
}
