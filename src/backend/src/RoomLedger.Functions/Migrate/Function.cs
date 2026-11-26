using Amazon.Lambda.Core;
using Npgsql;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.Migrate;

/**
 * Đầu vào của Lambda migrate: SQL sinh bằng dotnet ef migrations script --idempotent --no-transactions.
 * BootstrapPassword (mật khẩu master RDS) chỉ gửi ở lần chạy đầu trên database mới, để tạo các user đăng nhập bằng IAM.
 *
 * @author Tuấn
 * @version 1.1
 */
public record MigrateInput(string Sql, string? BootstrapPassword = null);

/**
 * Chạy SQL migration EF Core idempotent bằng roomledger_migrator, đăng nhập bằng RDS IAM token (trong VPC, không cần
 * endpoint Secrets Manager). Người deploy đọc secret master rồi gửi kèm một lần; Lambda không có quyền đọc secret,
 * không lưu và không ghi log mật khẩu. Script idempotent nên chạy lại nhiều lần vẫn an toàn.
 *
 * @author Tuấn
 * @version 1.2
 */
public class Function
{
    /**
     * Chạy toàn bộ script trong một transaction; lỗi thì rollback và ném ra để pipeline dừng deploy.
     *
     * @author Tuấn
     * @version 1.2
     */
    public async Task<string> HandleAsync(MigrateInput input, ILambdaContext context)
    {
        if (string.IsNullOrWhiteSpace(input.Sql))
        {
            throw new ArgumentException("Thiếu SQL migration");
        }

        var appUser = Environment.GetEnvironmentVariable("DB_APP_USER") ?? "roomledger_app";
        var migratorUser = Environment.GetEnvironmentVariable("DB_MIGRATOR_USER") ?? "roomledger_migrator";

        if (!string.IsNullOrEmpty(input.BootstrapPassword))
        {
            var master = ConnectionString(Environment.GetEnvironmentVariable("DB_MASTER_USER") ?? "postgres");
            master.Password = input.BootstrapPassword;
            master.Pooling = false;
            await using var admin = new NpgsqlConnection(master.ConnectionString);
            await admin.OpenAsync();
            await using var bootstrap = new NpgsqlCommand(BootstrapSql(appUser, migratorUser), admin);
            await bootstrap.ExecuteNonQueryAsync();
            context.Logger.LogInformation("Bootstrap database users finished");
        }

        var builder = new NpgsqlDataSourceBuilder(ConnectionString(migratorUser).ConnectionString);
        builder.UseRdsIamAuth(Environment.GetEnvironmentVariable("AWS_REGION")
            ?? throw new InvalidOperationException("Thiếu biến môi trường AWS_REGION (cần cho RDS IAM auth)"));
        await using var dataSource = builder.Build();

        await using var conn = await dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(input.Sql, conn, tx) { CommandTimeout = 300 })
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var grant = new NpgsqlCommand(GrantSql(appUser), conn, tx))
        {
            await grant.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        context.Logger.LogInformation("Migrate finished");
        return "OK";
    }

    private static NpgsqlConnectionStringBuilder ConnectionString(string user) => new()
    {
        Host = Environment.GetEnvironmentVariable("DB_HOST")
            ?? throw new InvalidOperationException("Thiếu biến môi trường DB_HOST"),
        Port = int.TryParse(Environment.GetEnvironmentVariable("DB_PORT"), out var port) ? port : 5432,
        Database = Environment.GetEnvironmentVariable("DB_NAME") ?? "roomledger",
        Username = user,
        SslMode = SslMode.Require,
    };

    /**
     * Chạy một lần bằng master: tạo user migrator (được DDL trong schema public) và user ứng dụng, cả hai đăng nhập
     * bằng RDS IAM, không mật khẩu. Chạy lại không đổi gì.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string BootstrapSql(string appUser, string migratorUser) => $$"""
        DO $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{migratorUser}}') THEN
            CREATE ROLE {{migratorUser}} LOGIN;
          END IF;
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appUser}}') THEN
            CREATE ROLE {{appUser}} LOGIN;
          END IF;
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rds_iam') THEN
            GRANT rds_iam TO {{migratorUser}};
            GRANT rds_iam TO {{appUser}};
          END IF;
        END $$;
        GRANT USAGE, CREATE ON SCHEMA public TO {{migratorUser}};
        GRANT USAGE ON SCHEMA public TO {{appUser}};
        """;

    /**
     * Sau mỗi lần migrate (chạy bằng migrator, chủ sở hữu bảng): cấp quyền tối thiểu cho user ứng dụng,
     * đọc ghi bảng nghiệp vụ, không DDL; audit_event chỉ INSERT và SELECT.
     *
     * @author Tuấn
     * @version 1.1
     */
    public static string GrantSql(string appUser) => $$"""
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {{appUser}};
        REVOKE UPDATE, DELETE ON audit_event FROM {{appUser}};
        REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM {{appUser}};
        """;
}
