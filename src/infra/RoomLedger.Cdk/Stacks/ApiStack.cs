using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.RDS;
using Constructs;
using EventBus = Amazon.CDK.AWS.Events.EventBus;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của ApiStack.
 *
 * @author Tuấn
 * @version 1.1
 */
public sealed class ApiStackProps : StackProps
{
    public required string Stage { get; init; }
    public required IVpc Vpc { get; init; }
    public required ISecurityGroup LambdaSg { get; init; }
    public required IDatabaseInstance Db { get; init; }
    public required StorageStack Storage { get; init; }
    public required AuthStack Auth { get; init; }
    /** Gói RoomLedger.Api (dotnet lambda package). */
    public required string ApiAssetPath { get; init; }
}

/**
 * API Gateway REST (Regional) với Cognito authorizer cho cả hai User Pool, Lambda api (.NET 8, ASP.NET Core) trong VPC.
 * Chỉ /api/v1/health và GET /api/v1/invites/{token} không cần token. Lambda photo-ingest, evidence-seal (Sang) gắn thêm sau.
 * Quyền IAM tối thiểu, không có "*" trong Action hay Resource.
 *
 * @author Tuấn
 * @version 1.1
 */
public sealed class ApiStack : Stack
{
    public RestApi Api { get; }
    public Function ApiFunction { get; }

    public ApiStack(Construct scope, string id, ApiStackProps props) : base(scope, id, props)
    {
        string Name(string n) => $"roomledger-{props.Stage}-{n}";
        var storage = props.Storage;
        var auth = props.Auth;
        var bus = EventBus.FromEventBusName(this, "Bus", "default");

        ApiFunction = new Function(this, "Api", new FunctionProps
        {
            FunctionName = Name("api"),
            Runtime = Runtime.DOTNET_8,
            // ASP.NET Core minimal API chạy bằng Amazon.Lambda.AspNetCoreServer.Hosting: handler là tên assembly
            Handler = "RoomLedger.Api",
            Code = Code.FromAsset(props.ApiAssetPath),
            MemorySize = 1024,
            Timeout = Duration.Seconds(29),
            Vpc = props.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = [props.LambdaSg],
            Tracing = Tracing.ACTIVE,
            Environment = new Dictionary<string, string>
            {
                ["ConnectionStrings__RoomLedger"] = AppConnectionString(props.Db),
                ["Database__UseIamAuth"] = "true",
                ["Storage__IncomingBucket"] = storage.Incoming.BucketName,
                ["Storage__EvidenceBucket"] = storage.Evidence.BucketName,
                ["Storage__DerivedBucket"] = storage.Derived.BucketName,
                ["EventBus__BusName"] = bus.EventBusName,
                ["EventBus__Source"] = "roomledger.api",
            },
            // Không đặt ReservedConcurrentExecutions: giới hạn tài khoản là 10
        });

        props.Db.GrantConnect(ApiFunction, DataStack.AppUser);
        // Presigned PUT ký bằng quyền của Lambda: chỉ cho ghi vào incoming, đọc 3 bucket để trả presigned GET
        storage.Incoming.GrantPut(ApiFunction);
        storage.Incoming.GrantRead(ApiFunction);
        storage.Evidence.GrantRead(ApiFunction);
        storage.Derived.GrantRead(ApiFunction);
        bus.GrantPutEventsTo(ApiFunction);

        Api = new RestApi(this, "Rest", new RestApiProps
        {
            RestApiName = Name("api"),
            EndpointTypes = [EndpointType.REGIONAL],
            DeployOptions = new StageOptions
            {
                StageName = props.Stage,
                TracingEnabled = true,
                ThrottlingRateLimit = 50,
                ThrottlingBurstLimit = 100,
            },
        });

        var authorizer = new CognitoUserPoolsAuthorizer(this, "Authorizer", new CognitoUserPoolsAuthorizerProps
        {
            AuthorizerName = Name("cognito"),
            CognitoUserPools = [auth.Users, auth.Admins],
        });
        var integration = new LambdaIntegration(ApiFunction);
        var secured = new MethodOptions { Authorizer = authorizer, AuthorizationType = AuthorizationType.COGNITO };

        var v1 = Api.Root.AddResource("api").AddResource("v1");
        v1.AddProxy(new ProxyResourceOptions { AnyMethod = false })
            .AddMethod("ANY", integration, secured);
        v1.AddResource("health").AddMethod("GET", integration);
        v1.AddResource("invites").AddResource("{token}").AddMethod("GET", integration);

        _ = new CfnOutput(this, "ApiUrl", new CfnOutputProps { Value = Api.Url });
    }

    /**
     * Connection string của user roomledger_app (không có mật khẩu: Lambda tự sinh RDS IAM token).
     *
     * @author Tuấn
     * @version 1.1
     */
    public static string AppConnectionString(IDatabaseInstance db) =>
        $"Host={db.DbInstanceEndpointAddress};Port={DataStack.DbPort};" +
        $"Database={DataStack.DbName};Username={DataStack.AppUser};SSL Mode=Require";
}
