using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.WAFv2;
using Constructs;

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
        string Name(string n) => Naming.Resource(props.Stage, n);
        var storage = props.Storage;
        var auth = props.Auth;

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
                ["Outbox__Bucket"] = storage.Outbox.BucketName,
                ["Outbox__Source"] = WorkflowStack.ApiSource,
            },
            // Không đặt ReservedConcurrentExecutions: giới hạn tài khoản là 10
        });

        props.Db.GrantConnect(ApiFunction, DataStack.AppUser);
        // Presigned PUT ký bằng quyền của Lambda: chỉ cho ghi vào incoming, đọc 3 bucket để trả presigned GET
        storage.Incoming.GrantPut(ApiFunction);
        storage.Incoming.GrantRead(ApiFunction);
        storage.Evidence.GrantRead(ApiFunction);
        storage.Derived.GrantRead(ApiFunction);
        // Mã QR nhận tiền của nhà trọ (xác nhận QR chép từ incoming sang derived/qr/*)
        ApiFunction.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "PutPropertyQr",
            Effect = Effect.ALLOW,
            Actions = ["s3:PutObject"],
            Resources = [storage.Derived.ArnForObjects("qr/*")],
        }));
        // Sự kiện nghiệp vụ: chỉ ghi được dưới tiền tố source của API trong outbox
        ApiFunction.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "PublishToOutbox",
            Effect = Effect.ALLOW,
            Actions = ["s3:PutObject"],
            Resources = [storage.Outbox.ArnForObjects($"events/{WorkflowStack.ApiSource}/*")],
        }));

        // D1 (BR-17): xóa ảnh trước khi khóa. Chỉ incoming, ảnh chuyển đổi trong derived (*.jpg gồm .thumb.jpg) và mã QR (derived/qr/*).
        // Không có quyền xóa trên evidence; PDF thanh lý (derived/reports/*.pdf) không khớp *.jpg nên không xóa được.
        ApiFunction.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "DeleteUnlockedPhotoObjects",
            Effect = Effect.ALLOW,
            Actions = ["s3:DeleteObject"],
            Resources = [storage.Incoming.ArnForObjects("*"), storage.Derived.ArnForObjects("*.jpg"), storage.Derived.ArnForObjects("qr/*")],
        }));
        
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

        AddWebAcl(props.Stage);
    }

    /** Số rule của Web ACL, cố định 5 (khớp dự toán: 1 Web ACL + 5 rule trong Pricing Calculator). */
    public const int WafRuleCount = 5;

    /** Giới hạn request mỗi IP trong 5 phút trước khi bị chặn. */
    private const int RateLimitPerFiveMinutes = 2000;

    /**
     * WAF Regional gắn vào stage của API Gateway, đúng 5 rule: bốn managed rule group của AWS
     * (Common, KnownBadInputs, SQLi, AmazonIpReputation) và một rate-based rule theo IP. Mặc định cho phép.
     */
    private void AddWebAcl(string stage)
    {
        CfnWebACL.RuleProperty Managed(string name, int priority) => new()
        {
            Name = name,
            Priority = priority,
            Statement = new CfnWebACL.StatementProperty
            {
                ManagedRuleGroupStatement = new CfnWebACL.ManagedRuleGroupStatementProperty { VendorName = "AWS", Name = name },
            },
            OverrideAction = new CfnWebACL.OverrideActionProperty { None = new Dictionary<string, object>() },
            VisibilityConfig = Visibility(name),
        };

        var rules = new[]
        {
            Managed("AWSManagedRulesCommonRuleSet", 1),
            Managed("AWSManagedRulesKnownBadInputsRuleSet", 2),
            Managed("AWSManagedRulesSQLiRuleSet", 3),
            Managed("AWSManagedRulesAmazonIpReputationList", 4),
            new CfnWebACL.RuleProperty
            {
                Name = "RateLimitPerIp",
                Priority = 5,
                Statement = new CfnWebACL.StatementProperty
                {
                    RateBasedStatement = new CfnWebACL.RateBasedStatementProperty
                    {
                        Limit = RateLimitPerFiveMinutes,
                        AggregateKeyType = "IP",
                    },
                },
                Action = new CfnWebACL.RuleActionProperty { Block = new Dictionary<string, object>() },
                VisibilityConfig = Visibility("RateLimitPerIp"),
            },
        };

        var webAcl = new CfnWebACL(this, "WebAcl", new CfnWebACLProps
        {
            Name = Naming.Resource(stage, "api-waf"),
            Scope = "REGIONAL",
            DefaultAction = new CfnWebACL.DefaultActionProperty { Allow = new Dictionary<string, object>() },
            Rules = rules,
            VisibilityConfig = Visibility(Naming.Resource(stage, "api-waf")),
        });

        var association = new CfnWebACLAssociation(this, "WebAclAssociation", new CfnWebACLAssociationProps
        {
            WebAclArn = webAcl.AttrArn,
            ResourceArn = $"arn:{Aws.PARTITION}:apigateway:{Region}::/restapis/{Api.RestApiId}/stages/{Api.DeploymentStage.StageName}",
        });
        association.Node.AddDependency(Api.DeploymentStage);
    }

    private static CfnWebACL.VisibilityConfigProperty Visibility(string metricName) => new()
    {
        CloudWatchMetricsEnabled = true,
        SampledRequestsEnabled = true,
        MetricName = metricName,
    };

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
