using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Lambda.EventSources;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SQS;
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
    public required string EvidenceBucketName { get; init; }
    /** Topic nhận báo động (ObservabilityStack); null → báo động không gửi đi đâu. */
    public Amazon.CDK.AWS.SNS.ITopic? AlarmTopic { get; init; }
}

/**
 * EventBridge + Lambda xử lý ảnh (T12). Sau này thêm read-document (AI đọc đồng hồ điện nước), notify.
 * Luồng upload: S3 "Object Created" (incoming) → photo-ingest → PhotoReceived → SQS photo-queue → photo-derive
 * (sơ đồ v8.5.2: hàng đợi đỡ đợt ảnh dồn dập, giới hạn số photo-derive chạy cùng lúc; lỗi 3 lần → DLQ).
 * Khóa biên bản: API InspectionLocked → evidence-seal (chép sang bucket evidence Object Lock, BR-18 RULE 4).
 * Trả phòng: không còn AI so ảnh (kiến trúc v8.5.2); API chuyển biên bản sang REVIEWING, chủ nhà xem ảnh và tự ghi
 * khoản trừ cọc. Sự kiện MoveOutSubmitted vẫn được phát để notify báo chủ nhà sau này.
 * Quyết toán (MD-06-FE-05): API TenancySettled → render-pdf (VPC, biên bản thanh lý PDF vào derived/reports/) và
 * evidence-seal (niêm phong ảnh biên bản trả phòng, detail có inspectionId).
 * Log của mọi Lambda ở đây có hạn giữ (30 ngày, prod 90); báo động DLQ gửi vào topic của ObservabilityStack.
 * Lambda trong VPC đăng nhập roomledger_app bằng IAM token; quyền S3 tối thiểu. Mọi rule có DLQ chung,
 * báo động khi DLQ có tin (chưa gắn SNS: chờ chọn email nhận báo động).
 * Phụ trách: Sang.
 *
 * @author Sang
 * @version 1.4
 */
public sealed class WorkflowStack : Stack
{
    public const string FunctionsSource = $"{Naming.Project}.functions";
    public const string PhotoReceived = "PhotoReceived";
    public const string ApiSource = $"{Naming.Project}.api";
    public const string InspectionLocked = "InspectionLocked";
    public const string MoveOutSubmitted = "MoveOutSubmitted";
    public const string TenancySettled = "TenancySettled";

    /** Hạn giữ log của Lambda theo stage. */
    public static RetentionDays LogRetentionFor(string stage) =>
        stage == "prod" ? RetentionDays.THREE_MONTHS : RetentionDays.ONE_MONTH;

    public Function PhotoIngest { get; }
    public Function PhotoDerive { get; }
    public Function EvidenceSeal { get; }
    public Function RenderPdf { get; }
    public Queue DeadLetterQueue { get; }
    public Queue PhotoQueue { get; }

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
            ["EVIDENCE_BUCKET"] = props.EvidenceBucketName,
            ["EVENT_BUS_NAME"] = "default",
            ["PHOTO_MAX_AGE_DAYS"] = "3",
        };

        // Log group tạo sẵn với hạn giữ (không để Lambda tự tạo, vì log tự tạo giữ mãi mãi)
        ILogGroup Logs(string handlerClass, string shortName) => new Amazon.CDK.AWS.Logs.LogGroup(this, $"{handlerClass}Logs", new Amazon.CDK.AWS.Logs.LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{Naming.Resource(props.Stage, shortName)}",
            Retention = LogRetentionFor(props.Stage),
            RemovalPolicy = props.Stage == "prod" ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY,
        });

        Function VpcFunction(string handlerClass, string shortName, double memory, Duration timeout, string method = "HandleAsync") =>
            new(this, handlerClass, new FunctionProps
            {
                FunctionName = Naming.Resource(props.Stage, shortName),
                LogGroup = Logs(handlerClass, shortName),
                Runtime = Runtime.DOTNET_8,
                Handler = $"RoomLedger.Functions::RoomLedger.Functions.{handlerClass}.Function::{method}",
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
        PhotoDerive = VpcFunction("PhotoDerive", "photo-derive", 2048, Duration.Minutes(2), "HandleQueueAsync"); // HEIC 12 MP ≈ 50 MB điểm ảnh
        // Biên bản tối đa ~20 ảnh/khu vực × vài khu vực; mỗi ảnh ≤ 25 MB đọc 2 lần để băm
        EvidenceSeal = VpcFunction("EvidenceSeal", "evidence-seal", 1024, Duration.Minutes(5));

        // Sự kiện không giao được sau khi thử lại (Lambda lỗi, state machine không khởi động được) vào đây
        DeadLetterQueue = new Queue(this, "WorkflowDlq", new QueueProps
        {
            QueueName = Naming.Resource(props.Stage, "workflow-dlq"),
            RetentionPeriod = Duration.Days(14),
            Encryption = QueueEncryption.SQS_MANAGED,
        });

        // Hàng đợi ảnh trước photo-derive. Visibility = 6 × timeout của hàm (khuyến nghị của AWS cho SQS → Lambda).
        // Lambda service tự poll SQS nên hàm trong VPC không cần VPC endpoint cho SQS.
        PhotoQueue = new Queue(this, "PhotoQueue", new QueueProps
        {
            QueueName = Naming.Resource(props.Stage, "photo-queue"),
            VisibilityTimeout = Duration.Minutes(12),
            RetentionPeriod = Duration.Days(4),
            Encryption = QueueEncryption.SQS_MANAGED,
            DeadLetterQueue = new DeadLetterQueue { Queue = DeadLetterQueue, MaxReceiveCount = 3 },
        });
        PhotoDerive.AddEventSource(new SqsEventSource(PhotoQueue, new SqsEventSourceProps
        {
            BatchSize = 5,
            MaxBatchingWindow = Duration.Seconds(5),
            ReportBatchItemFailures = true,
            MaxConcurrency = 2,   // giới hạn số photo-derive cùng lúc (RDS t4g.micro, account concurrency 10)
        }));

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

        // evidence-seal: đọc ảnh gốc (băm + nguồn CopyObject), ghi và đọc lại bản trong evidence.
        // Không cho xóa, đổi retention hay bypass Object Lock: bằng chứng chỉ ghi được một lần.
        var evidenceObjects = $"arn:{Aws.PARTITION}:s3:::{props.EvidenceBucketName}/*";
        EvidenceSeal.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:GetObject" },
            Resources = new[] { incomingObjects, evidenceObjects },
        }));
        EvidenceSeal.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:PutObject" },
            Resources = new[] { evidenceObjects },
        }));

        var defaultBus = EventBus.FromEventBusName(this, "DefaultBus", "default");
        defaultBus.GrantPutEventsTo(PhotoIngest);
        props.Db.GrantConnect(PhotoIngest, DataStack.AppUser);
        props.Db.GrantConnect(PhotoDerive, DataStack.AppUser);
        props.Db.GrantConnect(EvidenceSeal, DataStack.AppUser);

        var dlqAlarm = new Alarm(this, "WorkflowDlqNotEmpty", new AlarmProps
        {
            AlarmName = Naming.Resource(props.Stage, "workflow-dlq-not-empty"),
            AlarmDescription = "An EventBridge event could not be delivered to a workflow Lambda or state machine",
            Metric = DeadLetterQueue.MetricApproximateNumberOfMessagesVisible(new MetricOptions
            {
                Period = Duration.Minutes(5),
                Statistic = "Maximum",
            }),
            Threshold = 1,
            EvaluationPeriods = 1,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        });
        if (props.AlarmTopic is not null)
        {
            dlqAlarm.AddAlarmAction(new SnsAction(props.AlarmTopic));
        }

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
            Targets = new IRuleTarget[] { new LambdaFunction(PhotoIngest, new LambdaFunctionProps { RetryAttempts = 2, DeadLetterQueue = DeadLetterQueue }) },
        });

        new Rule(this, "PhotoReceivedRule", new RuleProps
        {
            Description = "photo-ingest done -> photo-queue (SQS) -> photo-derive",
            EventPattern = new EventPattern
            {
                Source = new[] { FunctionsSource },
                DetailType = new[] { PhotoReceived },
            },
            Targets = new IRuleTarget[] { new SqsQueue(PhotoQueue, new SqsQueueProps { RetryAttempts = 2, DeadLetterQueue = DeadLetterQueue }) },
        });

        new Rule(this, "InspectionLockedRule", new RuleProps
        {
            Description = "Inspection locked (API) -> evidence-seal",
            EventPattern = new EventPattern
            {
                Source = new[] { ApiSource },
                DetailType = new[] { InspectionLocked },
            },
            Targets = new IRuleTarget[] { new LambdaFunction(EvidenceSeal, new LambdaFunctionProps { RetryAttempts = 2, DeadLetterQueue = DeadLetterQueue }) },
        });

        // ---- MD-06-FE-05: quyết toán (Sang) ----
        // render-pdf: QuestPDF + font Lato đi kèm gói; đọc RDS, chỉ được ghi derived/reports/ (không đọc ảnh, không evidence)
        RenderPdf = VpcFunction("RenderPdf", "render-pdf", 1024, Duration.Minutes(1));
        RenderPdf.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:PutObject" },
            Resources = new[] { $"arn:{Aws.PARTITION}:s3:::{props.DerivedBucketName}/reports/*" },
        }));
        props.Db.GrantConnect(RenderPdf, DataStack.AppUser);

        new Rule(this, "TenancySettledRule", new RuleProps
        {
            Description = "Tenancy settled (API) -> render-pdf (termination record) + evidence-seal (move-out photos)",
            EventPattern = new EventPattern
            {
                Source = new[] { ApiSource },
                DetailType = new[] { TenancySettled },
            },
            Targets = new IRuleTarget[]
            {
                new LambdaFunction(RenderPdf, new LambdaFunctionProps { RetryAttempts = 2, DeadLetterQueue = DeadLetterQueue }),
                new LambdaFunction(EvidenceSeal, new LambdaFunctionProps { RetryAttempts = 2, DeadLetterQueue = DeadLetterQueue }),
            },
        });
    }
}
