using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RoomLedger.Functions.Migrate;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.Tests.Migrate;

/**
 * Database PostgreSQL tạm để kiểm tra Lambda migrate (giống ApiFactory: ROOMLEDGER_TEST_DB, mặc định docker compose).
 * Dùng đúng script CI sinh ra (dotnet ef migrations script --idempotent) và Function.GrantSql của Tuấn.
 * Giả lập RDS bằng role rds_iam; user ứng dụng có mật khẩu chỉ trong test (trên RDS dùng IAM token).
 *
 * @author Sang
 * @version 1.0
 */
public sealed class MigrateDbFixture : IAsyncLifetime
{
    public const string AppPassword = "test-only-password";

    private readonly string _baseCs;
    private readonly List<string> _databases = new();

    public string Database { get; } = NewDatabaseName();
    public string AppUser { get; } = "rl_app_" + Guid.NewGuid().ToString("N")[..8];
    public string Script { get; private set; } = "";

    public MigrateDbFixture()
    {
        _baseCs = Environment.GetEnvironmentVariable("ROOMLEDGER_TEST_DB")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
    }

    public static string NewDatabaseName() => "roomledger_test_" + Guid.NewGuid().ToString("N")[..12];

    public string MasterCs(string database) => new NpgsqlConnectionStringBuilder(_baseCs) { Database = database }.ConnectionString;

    public string AppCs => new NpgsqlConnectionStringBuilder(_baseCs)
    {
        Database = Database,
        Username = AppUser,
        Password = AppPassword,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync(Database);
        await ExecAsync(MasterCs("postgres"), """
            DO $$ BEGIN CREATE ROLE rds_iam NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $$;
            """);
        Script = GenerateIdempotentScript();
        await RunLikeHandleAsync(Database, Script);                                     // lần deploy đầu
        await ExecAsync(MasterCs(Database), $"ALTER ROLE {AppUser} PASSWORD '{AppPassword}';"); // chỉ trong test
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        foreach (var database in _databases)
        {
            await ExecAsync(MasterCs("postgres"), $"DROP DATABASE IF EXISTS {database} WITH (FORCE);");
        }
        await ExecAsync(MasterCs("postgres"), $"DROP ROLE IF EXISTS {AppUser};");
    }

    public async Task CreateDatabaseAsync(string database)
    {
        await ExecAsync(MasterCs("postgres"), $"CREATE DATABASE {database};");
        _databases.Add(database);
    }

    /**
     * Đúng các bước của Function.HandleAsync sau khi đọc secret: một transaction, chạy SQL, rồi GrantSql, rồi commit.
     *
     * @author Sang
     * @version 1.0
     */
    public async Task RunLikeHandleAsync(string database, string sql)
    {
        await using var conn = new NpgsqlConnection(MasterCs(database));
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 300 })
        {
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var grant = new NpgsqlCommand(Function.GrantSql(AppUser), conn, tx))
        {
            await grant.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public static async Task<object?> ScalarAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }

    public static async Task<int> ExecAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync();
    }

    /** Tùy chọn giống CI: dotnet ef migrations script --idempotent --no-transactions (backend.yml). */
    public const MigrationsSqlGenerationOptions CiScriptOptions =
        MigrationsSqlGenerationOptions.Idempotent | MigrationsSqlGenerationOptions.NoTransactions;

    public string GenerateIdempotentScript(MigrationsSqlGenerationOptions options = CiScriptOptions)
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(MasterCs(Database))
            .UseSnakeCaseNamingConvention()
            .Options;
        using var db = new AppDbContext(dbOptions, SystemCurrentUser.Instance, TimeProvider.System);
        return db.GetService<IMigrator>().GenerateScript(options: options);
    }
}

/**
 * Kiểm tra Lambda migrate (Migrate/Function.cs, Tuấn) trên PostgreSQL 16 thật:
 * quyền tối thiểu của user ứng dụng (BR-31: audit_event chỉ INSERT và SELECT), chạy lại an toàn, lỗi thì rollback.
 *
 * @author Sang
 * @version 1.0
 */
public class MigrateFunctionTests(MigrateDbFixture db) : IClassFixture<MigrateDbFixture>
{
    private const string InsertAudit = """
        INSERT INTO audit_event (id, event_type, target_type, target_id, created_at)
        VALUES ('{0}', 'TEST_EVENT', 'test', gen_random_uuid(), now());
        """;

    private Task<int> ExecAsApp(string sql) => MigrateDbFixture.ExecAsync(db.AppCs, sql);

    private async Task<Guid> InsertAuditAsApp()
    {
        var id = Guid.NewGuid();
        await ExecAsApp(string.Format(InsertAudit, id));
        return id;
    }

    private static async Task AssertPermissionDenied(Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(act);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState); // 42501
    }

    [Fact]
    public async Task App_can_insert_and_select_audit_events()
    {
        var id = await InsertAuditAsApp();
        var count = await MigrateDbFixture.ScalarAsync(db.AppCs, $"SELECT count(*) FROM audit_event WHERE id = '{id}';");
        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task App_cannot_update_audit_event()
    {
        var id = await InsertAuditAsApp();
        await AssertPermissionDenied(() => ExecAsApp($"UPDATE audit_event SET event_type = 'X' WHERE id = '{id}';"));
    }

    [Fact]
    public async Task App_cannot_delete_audit_event()
    {
        var id = await InsertAuditAsApp();
        await AssertPermissionDenied(() => ExecAsApp($"DELETE FROM audit_event WHERE id = '{id}';"));
    }

    [Fact]
    public async Task App_cannot_truncate_audit_event()
        => await AssertPermissionDenied(() => ExecAsApp("TRUNCATE audit_event;"));

    [Fact]
    public async Task App_can_insert_update_and_delete_business_tables()
    {
        var id = Guid.NewGuid();
        Assert.Equal(1, await ExecAsApp($"""
            INSERT INTO property (id, landlord_id, name, address, electricity_rate, water_rate, service_fee, created_at, updated_at)
            VALUES ('{id}', gen_random_uuid(), 'Nhà A', '1 Đường B', 3500, 20000, 100000, now(), now());
            """));
        Assert.Equal(1, await ExecAsApp($"UPDATE property SET name = 'Nhà A2' WHERE id = '{id}';"));
        Assert.Equal(1, await ExecAsApp($"DELETE FROM property WHERE id = '{id}';"));
    }

    [Fact]
    public async Task App_cannot_create_tables()
        => await AssertPermissionDenied(() => ExecAsApp("CREATE TABLE app_should_not_create (id int);"));

    [Fact]
    public async Task App_cannot_write_the_migrations_history()
    {
        // Không ghi tên cột: snake_case đổi MigrationId thành migration_id.
        await AssertPermissionDenied(() => ExecAsApp("""INSERT INTO "__EFMigrationsHistory" SELECT * FROM "__EFMigrationsHistory";"""));
        await AssertPermissionDenied(() => ExecAsApp("""DELETE FROM "__EFMigrationsHistory";"""));
    }

    [Fact]
    public async Task App_role_can_log_in_and_is_a_member_of_rds_iam()
    {
        var sql = $"""
            SELECT r.rolcanlogin AND pg_has_role(r.oid, 'rds_iam', 'MEMBER')
            FROM pg_roles r WHERE r.rolname = '{db.AppUser}';
            """;
        Assert.Equal(true, await MigrateDbFixture.ScalarAsync(db.MasterCs(db.Database), sql));
    }

    [Fact]
    public async Task Running_the_same_script_again_changes_nothing()
    {
        const string history = """SELECT count(*) FROM "__EFMigrationsHistory";""";
        var before = await MigrateDbFixture.ScalarAsync(db.MasterCs(db.Database), history);

        await db.RunLikeHandleAsync(db.Database, db.Script);   // deploy lần hai

        Assert.Equal(before, await MigrateDbFixture.ScalarAsync(db.MasterCs(db.Database), history));
        await InsertAuditAsApp();                              // quyền vẫn còn
        await AssertPermissionDenied(() => ExecAsApp("DELETE FROM audit_event;"));
    }

    [Fact]
    public async Task A_failing_script_leaves_the_database_unchanged()
    {
        // HandleAsync hứa: "lỗi thì rollback". Script lỗi ở cuối thì không bảng nào được tạo.
        var scratch = MigrateDbFixture.NewDatabaseName();
        await db.CreateDatabaseAsync(scratch);

        await Assert.ThrowsAsync<PostgresException>(() => db.RunLikeHandleAsync(scratch, db.Script + "\nSELECT 1/0;"));

        var tables = await MigrateDbFixture.ScalarAsync(db.MasterCs(scratch),
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';");
        Assert.Equal(0L, tables);
    }

    [Fact]
    public async Task A_script_with_its_own_transactions_breaks_HandleAsync()
    {
        // Lý do CI phải dùng --no-transactions: COMMIT trong script kết thúc transaction của Lambda.
        var scratch = MigrateDbFixture.NewDatabaseName();
        await db.CreateDatabaseAsync(scratch);
        var withTransactions = db.GenerateIdempotentScript(MigrationsSqlGenerationOptions.Idempotent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.RunLikeHandleAsync(scratch, withTransactions));
    }

    [Fact]
    public void Ci_generates_the_migration_script_without_its_own_transactions()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var workflow = File.ReadAllText(Path.Combine(dir!.FullName, ".github", "workflows", "backend.yml"));
        Assert.Contains("migrations script --idempotent --no-transactions", workflow);
    }
}
