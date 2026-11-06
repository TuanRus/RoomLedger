using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;

namespace RoomLedger.Functions.Migrate;

/**
 * Đầu vào của Lambda migrate: SQL do GitHub Actions sinh bằng dotnet ef migrations script --idempotent.
 *
 * @author Tuấn
 * @version 1.0
 */
public record MigrateInput(string Sql);

/**
 * Chạy SQL migration EF Core idempotent bằng roomledger_migrator (trong VPC).
 * Mật khẩu migrator lấy từ Secrets Manager (DB_SECRET_ARN) qua Interface endpoint; chỉ Lambda này có quyền đọc secret.
 * Script idempotent nên chạy lại nhiều lần vẫn an toàn.
 *
 * @author Tuấn
 * @version 1.1
 */
public class Function
{
    private static readonly AmazonSecretsManagerClient Secrets = new();

    /**
     * Chạy toàn bộ script trong một transaction; lỗi thì rollback và ném ra để pipeline dừng deploy.
     *
     * @author Tuấn
     * @version 1.1
     */
    public async Task<string> HandleAsync(MigrateInput input, ILambdaContext context)
    {
        if (string.IsNullOrWhiteSpace(input.Sql))
        {
            throw new ArgumentException("Thiếu SQL migration");
        }

        var secret = await Secrets.GetSecretValueAsync(new GetSecretValueRequest
        {
            SecretId = Environment.GetEnvironmentVariable("DB_SECRET_ARN"),
        });
        using var json = JsonDocument.Parse(secret.SecretString);
        var root = json.RootElement;
        var cs = new NpgsqlConnectionStringBuilder
        {
            Host = root.GetProperty("host").GetString(),
            Port = root.TryGetProperty("port", out var port) ? port.GetInt32() : 5432,
            Database = Environment.GetEnvironmentVariable("DB_NAME") ?? "roomledger",
            Username = root.GetProperty("username").GetString(),
            Password = root.GetProperty("password").GetString(),
            SslMode = SslMode.Require,
        };

        await using var conn = new NpgsqlConnection(cs.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(input.Sql, conn, tx) { CommandTimeout = 300 })
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var grant = new NpgsqlCommand(GrantSql(Environment.GetEnvironmentVariable("DB_APP_USER") ?? "roomledger_app"), conn, tx))
        {
            await grant.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        context.Logger.LogInformation("Migrate finished");
        return "OK";
    }

    /**
     * Tạo user ứng dụng (đăng nhập bằng RDS IAM, không mật khẩu) và cấp quyền tối thiểu:
     * đọc ghi bảng nghiệp vụ, không DDL; audit_event chỉ INSERT và SELECT.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string GrantSql(string appUser) => $$"""
        DO $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appUser}}') THEN
            CREATE ROLE {{appUser}} LOGIN;
          END IF;
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rds_iam') THEN
            GRANT rds_iam TO {{appUser}};
          END IF;
        END $$;
        GRANT USAGE ON SCHEMA public TO {{appUser}};
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {{appUser}};
        REVOKE UPDATE, DELETE ON audit_event FROM {{appUser}};
        REVOKE INSERT, UPDATE, DELETE ON "__EFMigrationsHistory" FROM {{appUser}};
        """;
}
