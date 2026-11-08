using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.RDS;
using Constructs;
using EventBus = Amazon.CDK.AWS.Events.EventBus;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của WorkflowStack. Bucket được truyền bằng tên (StorageStack của Phát tạo bucket và phải bật
 * EventBridge notifications cho bucket incoming), để hai stack không phụ thuộc vòng lẫn nhau.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class WorkflowStackProps : StackProps
{
    public required string Stage { get; init; }
    public required IVpc Vpc { get; init; }
    public required ISecurityGroup LambdaSg { get; init; }
    public required IDatabaseInstance Db { get; init; }
    public required string FunctionsAssetPath { get; init; }
    public required string IncomingBucketName { get; init; }
    public required string DerivedBucketName { get; init; }
}

/**
 * EventBridge + Lambda xử lý ảnh (T12). Sau này thêm Step Functions so ảnh, read-document, persist-results, notify.
 * Luồng upload: S3 "Object Created" (incoming) → photo-ingest → PhotoReceived → photo-derive.
 * Lambda chạy trong VPC (cập nhật bảng photo), đăng nhập roomledger_app bằng IAM token, quyền S3 tối thiểu.
 * Phụ trách: Sang.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class WorkflowStack : Stack
{
    public const string FunctionsSource = "roomledger.functions";
    public const string PhotoReceived = "PhotoReceived";

    public Function PhotoIngest { get; }
    public Function PhotoDerive { get; }

    public WorkflowStack(Construct scope, string id, WorkflowStackProps props) : base(scope, id, props)
    {
        var code = Code.FromAsset(props.FunctionsAssetPath);
        var environment = new Dictionary<string, string>
        {
            // Không có Password → FunctionDb dùng RDS IAM auth (token 15 phút) qua TLS
            ["ROOMLEDGER_DB"] = $"Host={props.Db.DbInstanceEndpointAddress};Port={DataStack.DbPort};Database={DataStack.DbName};" +
                                $"Username={DataStack.AppUser};SSL Mode=Require",
            ["INCOMING_BUCKET"] = props.IncomingBucketName,
            ["DERIVED_BUCKET"] = props.DerivedBucketName,
            ["EVENT_BUS_NAME"] = "default",
            ["PHOTO_MAX_AGE_DAYS"] = "3",
        };

        Function VpcFunction(string handlerClass, string shortName, double memory, Duration timeout) =>
            new(this, handlerClass, new FunctionProps
            {
                FunctionName = $"roomledger-{props.Stage}-{shortName}",
                Runtime = Runtime.DOTNET_8,
                Handler = $"RoomLedger.Functions::RoomLedger.Functions.{handlerClass}.Function::HandleAsync",
                Code = code,
                MemorySize = memory,
                Timeout = timeout,
                Vpc = props.Vpc,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
                SecurityGroups = new[] { props.LambdaSg },
                Environment = environment,
                // Không đặt ReservedConcurrentExecutions: giới hạn tài khoản là 10
            });

        PhotoIngest = VpcFunction("PhotoIngest", "photo-ingest", 512, Duration.Seconds(60));
        PhotoDerive = VpcFunction("PhotoDerive", "photo-derive", 2048, Duration.Minutes(2)); // HEIC 12 MP ≈ 50 MB điểm ảnh

        var incomingObjects = $"arn:{Aws.PARTITION}:s3:::{props.IncomingBucketName}/*";
        var derivedObjects = $"arn:{Aws.PARTITION}:s3:::{props.DerivedBucketName}/*";
        PhotoIngest.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:GetObject" },
            Resources = new[] { incomingObjects },
        }));
        PhotoDerive.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:GetObject" },
            Resources = new[] { incomingObjects },
        }));
        PhotoDerive.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:PutObject" },
            Resources = new[] { derivedObjects },
        }));

        EventBus.FromEventBusName(this, "DefaultBus", "default").GrantPutEventsTo(PhotoIngest);
        props.Db.GrantConnect(PhotoIngest, DataStack.AppUser);
        props.Db.GrantConnect(PhotoDerive, DataStack.AppUser);

        new Rule(this, "PhotoUploaded", new RuleProps
        {
            Description = "New photo in the incoming bucket -> photo-ingest",
            EventPattern = new EventPattern
            {
                Source = new[] { "aws.s3" },
                DetailType = new[] { "Object Created" },
                Detail = new Dictionary<string, object>
                {
                    ["bucket"] = new Dictionary<string, object> { ["name"] = new[] { props.IncomingBucketName } },
                },
            },
            Targets = new IRuleTarget[] { new LambdaFunction(PhotoIngest, new LambdaFunctionProps { RetryAttempts = 2 }) },
        });

        new Rule(this, "PhotoReceivedRule", new RuleProps
        {
            Description = "photo-ingest done -> photo-derive",
            EventPattern = new EventPattern
            {
                Source = new[] { FunctionsSource },
                DetailType = new[] { PhotoReceived },
            },
            Targets = new IRuleTarget[] { new LambdaFunction(PhotoDerive, new LambdaFunctionProps { RetryAttempts = 2 }) },
        });
    }
}
