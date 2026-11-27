using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.RDS;
using Constructs;
using InstanceType = Amazon.CDK.AWS.EC2.InstanceType;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của DataStack.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class DataStackProps : StackProps
{
    public required string Stage { get; init; }
    public required IVpc Vpc { get; init; }
    public required ISecurityGroup LambdaSg { get; init; }
    /** Thư mục hoặc file zip của RoomLedger.Functions đã đóng gói (dotnet lambda package). */
    public required string FunctionsAssetPath { get; init; }
}

/**
 * RDS PostgreSQL 16 (private, IAM auth, bắt buộc SSL) và Lambda migrate.
 * Lambda migrate (Migrate/Function.cs của Tuấn) nhận SQL idempotent, đăng nhập roomledger_migrator bằng IAM token,
 * chạy SQL rồi GrantSql cho roomledger_app. Lần đầu người deploy gửi kèm mật khẩu master để tạo hai user này.
 * Các Lambda khác đăng nhập roomledger_app bằng IAM token.
 * Phụ trách: Tuấn (Sang viết bản đầu, T6).
 *
 * @author Sang
 * @version 1.0
 */
public sealed class DataStack : Stack
{
    public const string DbName = Naming.Project;
    public const int DbPort = 5432;
    /** User ứng dụng (đăng nhập bằng IAM token), do Lambda migrate tạo và cấp quyền. */
    public const string AppUser = $"{Naming.Project}_app";

    /** User chạy migration (IAM token), do bước bootstrap của Lambda migrate tạo. */
    public const string MigratorUser = $"{Naming.Project}_migrator";
    private const string MasterUser = "postgres";

    public DatabaseInstance Db { get; }
    public Function Migrate { get; }

    public DataStack(Construct scope, string id, DataStackProps props) : base(scope, id, props)
    {
        var isDev = props.Stage == "dev";
        var isolated = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED };
        var engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps { Version = PostgresEngineVersion.VER_16 });

        Db = new DatabaseInstance(this, "Db", new DatabaseInstanceProps // không đổi id "Db" (sẽ tạo database mới)
        {
            InstanceIdentifier = $"{Naming.Project}-{props.Stage}",
            Engine = engine,
            InstanceType = InstanceType.Of(InstanceClass.BURSTABLE4_GRAVITON, InstanceSize.MICRO),
            Vpc = props.Vpc,
            VpcSubnets = isolated,
            DatabaseName = DbName,
            Port = DbPort, // ghi rõ: token IAM được ký cho cổng này
            MultiAz = props.Stage == "prod",   // sơ đồ v8.5.2: standby ở AZ thứ hai cho production; dev/demo một AZ cho rẻ
            AllocatedStorage = 20,
            StorageEncrypted = true,
            IamAuthentication = true,
            PubliclyAccessible = false,
            Credentials = Credentials.FromGeneratedSecret(MasterUser),
            ParameterGroup = new ParameterGroup(this, "Pg", new ParameterGroupProps
            {
                Engine = engine,
                Parameters = new Dictionary<string, string> { ["rds.force_ssl"] = "1" },
            }),
            BackupRetention = Duration.Days(isDev ? 1 : 7),
            DeletionProtection = !isDev,
            RemovalPolicy = isDev ? RemovalPolicy.DESTROY : RemovalPolicy.SNAPSHOT,
        });
        // Cổng ghi số (AllowDefaultPortFrom sinh token GetAtt thay vì 5432).
        Db.Connections.AllowFrom(props.LambdaSg, Port.Tcp(DbPort), "PostgreSQL from RoomLedger Lambdas");

        Migrate = new Function(this, "Migrate", new FunctionProps
        {
            FunctionName = Naming.Resource(props.Stage, "migrate"),
            Runtime = Runtime.DOTNET_8,
            Architecture = Architecture.ARM_64,
            Handler = "RoomLedger.Functions::RoomLedger.Functions.Migrate.Function::HandleAsync",
            Code = Code.FromAsset(props.FunctionsAssetPath),
            MemorySize = 512,
            Timeout = Duration.Minutes(2),
            Vpc = props.Vpc,
            VpcSubnets = isolated,
            SecurityGroups = new[] { props.LambdaSg },
            // Không đặt ReservedConcurrentExecutions: giới hạn tài khoản là 10.
            // Đúng các biến Migrate/Function.cs (Tuấn) đọc; không có mật khẩu nào ở đây.
            Environment = new Dictionary<string, string>
            {
                ["DB_HOST"] = Db.DbInstanceEndpointAddress,
                ["DB_PORT"] = DbPort.ToString(),
                ["DB_NAME"] = DbName,
                ["DB_APP_USER"] = AppUser,
                ["DB_MIGRATOR_USER"] = MigratorUser,
                ["DB_MASTER_USER"] = MasterUser,
            },
        });
        Db.GrantConnect(Migrate, MigratorUser);

        new CfnOutput(this, "DbEndpoint", new CfnOutputProps { Value = Db.DbInstanceEndpointAddress });
        new CfnOutput(this, "DbSecretArn", new CfnOutputProps { Value = Db.Secret!.SecretArn });   // người deploy đọc cho lần bootstrap
        new CfnOutput(this, "MigrateFunctionName", new CfnOutputProps { Value = Migrate.FunctionName });
    }
}
