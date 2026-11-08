using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

using RoomLedger.Cdk.Stacks;

namespace RoomLedger.Cdk.Tests;

/// <summary>T6 — Data stack: RDS PostgreSQL 16 + migrate Lambda.</summary>
public class DataStackTests
{
    private static readonly Lazy<Template> DevTemplate = new(() => Build("dev"));

    private static Template Synth(string stage = "dev") => stage == "dev" ? DevTemplate.Value : Build(stage);

    private static Template Build(string stage)
    {
        var app = new App();
        var env = new Amazon.CDK.Environment { Account = "123456789012", Region = "ap-southeast-2" };
        var network = new NetworkStack(app, "TestNetwork", new NetworkStackProps { Stage = stage, Env = env });

        // Stand-in for the packaged RoomLedger.Functions zip (real one: artifacts/functions).
        var asset = Path.Combine(Path.GetTempPath(), "roomledger-test-functions");
        Directory.CreateDirectory(asset);
        File.WriteAllText(Path.Combine(asset, "placeholder.txt"), "test");

        var data = new DataStack(app, "TestData", new DataStackProps
        {
            Stage = stage,
            Env = env,
            Vpc = network.Vpc,
            LambdaSg = network.LambdaSg,
            FunctionsAssetPath = asset,
        });
        return Template.FromStack(data);
    }

    private static IDictionary<string, object> Single(Template t, string type)
    {
        var all = t.FindResources(type);
        Assert.Single(all);
        return all.Values.Single();
    }

    private static IDictionary<string, object> PropsOf(IDictionary<string, object> resource)
        => (IDictionary<string, object>)resource["Properties"];

    private static string Json(object o) => JsonSerializer.Serialize(o);

    // ---------- RDS ----------

    [Fact]
    public void Db_is_postgres_16_on_t4g_micro_single_az()
    {
        var db = PropsOf(Single(Synth(), "AWS::RDS::DBInstance"));
        Assert.Equal("postgres", db["Engine"]);
        Assert.StartsWith("16", (string)db["EngineVersion"]);
        Assert.Equal("db.t4g.micro", db["DBInstanceClass"]);
        Assert.False((bool)db["MultiAZ"]);
        Assert.Equal("roomledger", db["DBName"]);
        Assert.Equal("roomledger-dev", db["DBInstanceIdentifier"]);
    }

    [Fact]
    public void Db_is_private_encrypted_and_uses_iam_auth()
    {
        var db = PropsOf(Single(Synth(), "AWS::RDS::DBInstance"));
        Assert.False((bool)db["PubliclyAccessible"]);
        Assert.True((bool)db["StorageEncrypted"]);
        Assert.True((bool)db["EnableIAMDatabaseAuthentication"]);
    }

    [Fact]
    public void Db_forces_ssl_through_parameter_group()
        => Synth().HasResourceProperties("AWS::RDS::DBParameterGroup", new Dictionary<string, object>
        {
            ["Parameters"] = Match.ObjectLike(new Dictionary<string, object> { ["rds.force_ssl"] = "1" }),
        });

    [Fact]
    public void Db_lives_in_two_isolated_subnets()
    {
        var group = PropsOf(Single(Synth(), "AWS::RDS::DBSubnetGroup"));
        Assert.Equal(2, ((object[])group["SubnetIds"]).Length);
    }

    [Fact]
    public void Master_password_is_a_generated_secret()
    {
        var t = Synth();
        t.ResourceCountIs("AWS::SecretsManager::Secret", 1);
        var db = PropsOf(Single(t, "AWS::RDS::DBInstance"));
        Assert.Contains("resolve:secretsmanager", Json(db["MasterUserPassword"]));
    }

    [Fact]
    public void Db_port_5432_is_open_to_lambda_sg_only()
    {
        var t = Synth();
        var ingress = t.FindResources("AWS::EC2::SecurityGroupIngress").Values.Select(PropsOf).ToList();
        var dbRules = ingress.Where(r => Convert.ToInt32(r["FromPort"]) == 5432).ToList();
        Assert.Single(dbRules);
        Assert.Equal(5432, Convert.ToInt32(dbRules[0]["ToPort"]));
        Assert.Contains("LambdaSg", Json(dbRules[0]["SourceSecurityGroupId"]));
        Assert.DoesNotContain(ingress, r => r.ContainsKey("CidrIp"));
    }

    [Theory]
    [InlineData("dev", "Delete")]
    [InlineData("demo", "Snapshot")]
    public void Removal_policy_depends_on_stage(string stage, string expected)
    {
        var db = Single(Synth(stage), "AWS::RDS::DBInstance");
        Assert.Equal(expected, db["DeletionPolicy"]);
    }

    // ---------- migrate Lambda ----------

    private static IDictionary<string, object> MigrateFn(Template t)
        => t.FindResources("AWS::Lambda::Function").Values.Select(PropsOf)
            .Single(p => p.TryGetValue("FunctionName", out var n) && n as string == "roomledger-dev-migrate");

    [Fact]
    public void Migrate_function_runs_dotnet8_with_the_right_handler()
    {
        var fn = MigrateFn(Synth());
        Assert.Equal("dotnet8", fn["Runtime"]);
        // Hợp đồng của Tuấn: Function.HandleAsync(MigrateInput { Sql }, ILambdaContext).
        Assert.Equal("RoomLedger.Functions::RoomLedger.Functions.Migrate.Function::HandleAsync", fn["Handler"]);
        Assert.Equal(512, Convert.ToInt32(fn["MemorySize"]));
        Assert.Equal(120, Convert.ToInt32(fn["Timeout"]));
    }

    [Fact]
    public void Migrate_function_runs_inside_the_vpc_with_lambda_sg()
    {
        var vpcConfig = (IDictionary<string, object>)MigrateFn(Synth())["VpcConfig"];
        Assert.Equal(2, ((object[])vpcConfig["SubnetIds"]).Length);
        Assert.Contains("LambdaSg", Json(vpcConfig["SecurityGroupIds"]));
    }

    [Fact]
    public void Migrate_function_gets_exactly_the_env_tuans_function_reads()
    {
        // Migrate/Function.cs đọc DB_SECRET_ARN (host lấy từ secret), DB_NAME, DB_APP_USER.
        var env = (IDictionary<string, object>)((IDictionary<string, object>)MigrateFn(Synth())["Environment"])["Variables"];
        Assert.Equal(new[] { "DB_APP_USER", "DB_NAME", "DB_SECRET_ARN" }, env.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Contains("Secret", Json(env["DB_SECRET_ARN"]));
        Assert.Equal("roomledger", env["DB_NAME"]);
        Assert.Equal("roomledger_app", env["DB_APP_USER"]);
    }

    [Fact]
    public void Migrate_function_has_no_reserved_concurrency_while_account_limit_is_10()
        => Assert.False(MigrateFn(Synth()).ContainsKey("ReservedConcurrentExecutions"));

    [Fact]
    public void Migrate_role_reads_the_master_secret_but_has_no_iam_db_login()
    {
        // Migrate đăng nhập bằng mật khẩu trong secret; quyền rds-db:connect thừa thì không cấp.
        var policies = Json(Synth().FindResources("AWS::IAM::Policy"));
        Assert.Contains("secretsmanager:GetSecretValue", policies);
        Assert.DoesNotContain("rds-db:connect", policies);
    }
}
