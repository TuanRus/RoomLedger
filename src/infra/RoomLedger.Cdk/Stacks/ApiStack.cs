using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * API Gateway REST (Regional) với Cognito authorizer cho cả hai User Pool, Lambda api (.NET 8, ASP.NET Core) trong VPC.
 * Chỉ /api/v1/health và GET /api/v1/invites/{token} không cần token. Lambda photo-ingest, evidence-seal (Sang) gắn thêm sau.
 * Quyền IAM tối thiểu, không có "*" trong Action hay Resource.
 *
 * @author Tuấn
 * @version 1.0
 */
public class ApiStack : Stack
{
    public RestApi Api { get; }
    public Function ApiFunction { get; }

    public ApiStack(Construct scope, string id, StageConfig cfg, NetworkStack network, StorageStack storage, DataStack data,
        AuthStack auth, string apiCodePath, IStackProps? props = null) : base(scope, id, props)
    {
        var bus = EventBus.FromEventBusName(this, "Bus", "default");

        ApiFunction = new Function(this, "Api", new FunctionProps
        {
            FunctionName = cfg.Name("api"),
            Runtime = Runtime.DOTNET_8,
            Architecture = Architecture.ARM_64,
            // ASP.NET Core minimal API chạy bằng Amazon.Lambda.AspNetCoreServer.Hosting: handler là tên assembly
            Handler = "RoomLedger.Api",
            Code = Code.FromAsset(apiCodePath),
            MemorySize = 1024,
            Timeout = Duration.Seconds(29),
            Vpc = network.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = [network.LambdaSecurityGroup],
            Tracing = Tracing.ACTIVE,
            Environment = new Dictionary<string, string>
            {
                ["ConnectionStrings__RoomLedger"] = AppConnectionString(data),
                ["Database__UseIamAuth"] = "true",
                ["Storage__IncomingBucket"] = storage.Incoming.BucketName,
                ["Storage__EvidenceBucket"] = storage.Evidence.BucketName,
                ["Storage__DerivedBucket"] = storage.Derived.BucketName,
                ["EventBus__BusName"] = bus.EventBusName,
                ["EventBus__Source"] = "roomledger.api",
            },
        });

        GrantDbConnect(ApiFunction, data, this);
        // Presigned PUT ký bằng quyền của Lambda: chỉ cho ghi vào incoming, đọc 3 bucket để trả presigned GET
        storage.Incoming.GrantPut(ApiFunction);
        storage.Incoming.GrantRead(ApiFunction);
        storage.Evidence.GrantRead(ApiFunction);
        storage.Derived.GrantRead(ApiFunction);
        bus.GrantPutEventsTo(ApiFunction);

        Api = new RestApi(this, "Rest", new RestApiProps
        {
            RestApiName = cfg.Name("api"),
            EndpointTypes = [EndpointType.REGIONAL],
            DeployOptions = new StageOptions
            {
                StageName = cfg.Stage,
                TracingEnabled = true,
                ThrottlingRateLimit = 50,
                ThrottlingBurstLimit = 100,
            },
        });

        var authorizer = new CognitoUserPoolsAuthorizer(this, "Authorizer", new CognitoUserPoolsAuthorizerProps
        {
            AuthorizerName = cfg.Name("cognito"),
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
     * @version 1.0
     */
    public static string AppConnectionString(DataStack data) =>
        $"Host={data.Database.DbInstanceEndpointAddress};Port={data.Database.DbInstanceEndpointPort};" +
        $"Database={DataStack.DatabaseName};Username={DataStack.AppUser};SSL Mode=Require";

    /**
     * Cho Lambda quyền rds-db:connect đúng một user roomledger_app của đúng một instance.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void GrantDbConnect(Function fn, DataStack data, Stack stack) =>
        fn.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["rds-db:connect"],
            Resources =
            [
                $"arn:{stack.Partition}:rds-db:{stack.Region}:{stack.Account}:dbuser:{data.Database.InstanceResourceId}/{DataStack.AppUser}",
            ],
        }));
}
