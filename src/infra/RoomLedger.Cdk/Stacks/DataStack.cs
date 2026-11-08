using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.RDS;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * RDS PostgreSQL trong private subnet (IAM auth, rds.force_ssl = 1) và Lambda migrate.
 * Master user là roomledger_migrator, mật khẩu nằm trong Secrets Manager và chỉ Lambda migrate đọc được.
 * Lambda ứng dụng dùng user roomledger_app qua RDS IAM auth (migrate tạo user này).
 *
 * @author Tuấn
 * @version 1.0
 */
public class DataStack : Stack
{
    public const string DatabaseName = "roomledger";
    public const string AppUser = "roomledger_app";

    public DatabaseInstance Database { get; }

    public DataStack(Construct scope, string id, StageConfig cfg, NetworkStack network, string functionsCodePath, IStackProps? props = null)
        : base(scope, id, props)
    {
        var parameters = new ParameterGroup(this, "Params", new ParameterGroupProps
        {
            Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps { Version = PostgresEngineVersion.VER_16 }),
            Parameters = new Dictionary<string, string> { ["rds.force_ssl"] = "1" },
        });

        Database = new DatabaseInstance(this, "Db", new DatabaseInstanceProps
        {
            InstanceIdentifier = $"roomledger-{cfg.Stage}",
            Engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps { Version = PostgresEngineVersion.VER_16 }),
            InstanceType = Amazon.CDK.AWS.EC2.InstanceType.Of(InstanceClass.BURSTABLE4_GRAVITON, InstanceSize.MICRO),
            Vpc = network.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = [network.RdsSecurityGroup],
            DatabaseName = DatabaseName,
            Credentials = Credentials.FromGeneratedSecret("roomledger_migrator", new CredentialsBaseOptions
            {
                SecretName = $"/roomledger/{cfg.Stage}/db/migrator",
            }),
            IamAuthentication = true,
            ParameterGroup = parameters,
            AllocatedStorage = 20,
            StorageEncrypted = true,
            MultiAz = false,
            BackupRetention = Duration.Days(cfg.Stage == "demo" ? 7 : 1),
            DeletionProtection = cfg.Stage == "demo",
            RemovalPolicy = cfg.Stage == "demo" ? RemovalPolicy.SNAPSHOT : RemovalPolicy.DESTROY,
        });

        var migrate = new Function(this, "Migrate", new FunctionProps
        {
            FunctionName = cfg.Name("migrate"),
            Runtime = Runtime.DOTNET_8,
            Architecture = Architecture.ARM_64,
            Handler = "RoomLedger.Functions::RoomLedger.Functions.Migrate.Function::HandleAsync",
            Code = Code.FromAsset(functionsCodePath),
            MemorySize = 512,
            Timeout = Duration.Minutes(5),
            Vpc = network.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = [network.LambdaSecurityGroup],
            Tracing = Tracing.ACTIVE,
            Environment = new Dictionary<string, string>
            {
                ["DB_SECRET_ARN"] = Database.Secret!.SecretArn,
                ["DB_NAME"] = DatabaseName,
                ["DB_APP_USER"] = AppUser,
            },
        });
        Database.Secret.GrantRead(migrate);

        _ = new CfnOutput(this, "MigrateFunctionName", new CfnOutputProps { Value = migrate.FunctionName });
    }
}
