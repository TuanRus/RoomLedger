using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using RoomLedger.Core.Abstractions;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Api.Tests.Infrastructure;

/**
 * Chạy API thật trên một database PostgreSQL tạm (tạo bằng migration, xóa khi xong).
 * Mặc định dùng PostgreSQL của docker compose; CI đặt ROOMLEDGER_TEST_DB tới service postgres.
 * S3 và EventBridge được thay bằng bản giả để không gọi AWS.
 *
 * @author Tuấn
 * @version 1.0
 */
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString;
    private readonly string _adminConnectionString;
    private readonly string _database = "roomledger_test_" + Guid.NewGuid().ToString("N")[..12];

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };

    public FakeEventPublisher Events { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero));

    public ApiFactory()
    {
        var baseCs = Environment.GetEnvironmentVariable("ROOMLEDGER_TEST_DB")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
        _adminConnectionString = new NpgsqlConnectionStringBuilder(baseCs) { Database = "postgres" }.ConnectionString;
        _connectionString = new NpgsqlConnectionStringBuilder(baseCs) { Database = _database }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:RoomLedger", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStorage, FakeStorage>();
            services.AddSingleton<IEventPublisher>(Events);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_adminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /**
     * HttpClient đăng nhập như một người dùng (sub, email đã xác minh, group).
     *
     * @author Tuấn
     * @version 1.0
     */
    public HttpClient ClientFor(TestUser user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, user.Header);
        return client;
    }

    /**
     * Chạy code với DbContext hệ thống (không bị query filter) để chuẩn bị hoặc kiểm tra dữ liệu.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new AppDbContext(options, SystemCurrentUser.Instance, Clock);
        return await action(db);
    }
}

/**
 * Người dùng giả cho test.
 *
 * @author Tuấn
 * @version 1.0
 */
public record TestUser(Guid Id, string Email, string Group, string Name = "Người dùng test", bool EmailVerified = true)
{
    public string Header => $"{Id}|{Email}|{Group}|{EmailVerified}|{Name}";

    public static TestUser Landlord(string email = "landlord@example.com") => new(Guid.NewGuid(), email, "landlord", "Chủ nhà A");

    public static TestUser Tenant(string email = "tenant@example.com") => new(Guid.NewGuid(), email, "tenant", "Người thuê B");
}

/**
 * Đọc phản hồi chuẩn {success, message, data} hoặc lỗi.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class ResponseExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<JsonElement> DataAsync(this HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("data");

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, ApiFactory.Json);

    public static Task<HttpResponseMessage> PatchJsonAsync(this HttpClient client, string url, object body) =>
        client.PatchAsJsonAsync(url, body, ApiFactory.Json);
}
