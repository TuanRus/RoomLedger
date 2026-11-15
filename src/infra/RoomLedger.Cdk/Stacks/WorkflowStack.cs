using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.SSM;
using Constructs;
using EventBus = Amazon.CDK.AWS.Events.EventBus;
using Sfn = Amazon.CDK.AWS.StepFunctions;
using Tasks = Amazon.CDK.AWS.StepFunctions.Tasks;

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
}

/**
 * EventBridge + Lambda xử lý ảnh (T12) và Step Functions so ảnh trả phòng (MD-06-FE-02). Sau này thêm read-document, notify.
 * Luồng upload: S3 "Object Created" (incoming) → photo-ingest → PhotoReceived → photo-derive.
 * Khóa biên bản: API InspectionLocked → evidence-seal (chép sang bucket evidence Object Lock, BR-18 RULE 4).
 * Trả phòng: API MoveOutSubmitted → state machine move-out-compare: đọc ngưỡng pHash và giới hạn gọi từ SSM →
 * plan-compare (VPC) → Map compare-pairs (ngoài VPC, Bedrock, tối đa 3 song song; lỗi → NOT_ANALYZED) →
 * persist-results (VPC, COMPARING → REVIEWING) → PutMetricData RoomLedger/Compare → sự kiện MoveOutCompared (notify).
 * Lambda trong VPC đăng nhập roomledger_app bằng IAM token; quyền S3 / Bedrock tối thiểu. Mọi rule có DLQ chung,
 * báo động khi DLQ có tin hoặc state machine chạy lỗi (chưa gắn SNS: chờ chọn email nhận báo động).
 * Phụ trách: Sang.
 *
 * @author Sang
 * @version 1.1
 */
public sealed class WorkflowStack : Stack
{
    public const string FunctionsSource = "roomledger.functions";
    public const string PhotoReceived = "PhotoReceived";
    public const string ApiSource = "roomledger.api";
    public const string InspectionLocked = "InspectionLocked";
    public const string MoveOutSubmitted = "MoveOutSubmitted";
    public const string MoveOutCompared = "MoveOutCompared";
    public const string PrimaryModel = "au.anthropic.claude-sonnet-4-5-20250929-v1:0";
    public const string FallbackModel = "au.anthropic.claude-haiku-4-5-20251001-v1:0";
    public const int CompareConcurrency = 3;   // quota Bedrock của tài khoản mới thấp; Map chạy tối đa 3 cặp cùng lúc

    public Function PhotoIngest { get; }
    public Function PhotoDerive { get; }
    public Function EvidenceSeal { get; }
    public Function PlanCompare { get; }
    public Function ComparePairs { get; }
    public Function PersistResults { get; }
    public Sfn.StateMachine CompareStateMachine { get; }
    public Queue DeadLetterQueue { get; }

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
        // Biên bản tối đa ~20 ảnh/khu vực × vài khu vực; mỗi ảnh ≤ 25 MB đọc 2 lần để băm
        EvidenceSeal = VpcFunction("EvidenceSeal", "evidence-seal", 1024, Duration.Minutes(5));

        // Sự kiện không giao được sau khi thử lại (Lambda lỗi, state machine không khởi động được) vào đây
        DeadLetterQueue = new Queue(this, "WorkflowDlq", new QueueProps
        {
            QueueName = $"roomledger-{props.Stage}-workflow-dlq",
            RetentionPeriod = Duration.Days(14),
            Encryption = QueueEncryption.SQS_MANAGED,
        });

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

        // ---- MD-06-FE-02: so ảnh trả phòng (Sang) ----
        PlanCompare = VpcFunction("PlanCompare", "plan-compare", 1024, Duration.Minutes(2));   // pHash thumbnail 400 px
        PersistResults = VpcFunction("PersistResults", "persist-results", 512, Duration.Seconds(60));
        // Ngoài VPC: VPC không có NAT, Bedrock không có VPC endpoint. Không có ROOMLEDGER_DB, không đọc database.
        ComparePairs = new Function(this, "ComparePairs", new FunctionProps
        {
            FunctionName = $"roomledger-{props.Stage}-compare-pairs",
            Runtime = Runtime.DOTNET_8,
            Handler = "RoomLedger.Functions::RoomLedger.Functions.ComparePairs.Function::HandleAsync",
            Code = code,
            MemorySize = 512,
            Timeout = Duration.Minutes(3),   // hai mô hình × thử lại của SDK, mỗi lần gửi hai ảnh
            Environment = new Dictionary<string, string>
            {
                ["DERIVED_BUCKET"] = props.DerivedBucketName,
                ["BEDROCK_PRIMARY_MODEL"] = PrimaryModel,
                ["BEDROCK_FALLBACK_MODEL"] = FallbackModel,
            },
        });

        PlanCompare.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:GetObject" },
            Resources = new[] { derivedObjects },
        }));
        ComparePairs.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:GetObject" },
            Resources = new[] { derivedObjects },
        }));
        // Inference profile "au." chuyển yêu cầu sang các vùng ở Úc → cần quyền trên profile và trên model gốc ở mọi vùng đó
        ComparePairs.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "bedrock:InvokeModel" },
            Resources = new[] { PrimaryModel, FallbackModel }.SelectMany(m => new[]
            {
                $"arn:{Aws.PARTITION}:bedrock:{Region}:{Account}:inference-profile/{m}",
                $"arn:{Aws.PARTITION}:bedrock:*::foundation-model/{m["au.".Length..]}",
            }).ToArray(),
        }));
        props.Db.GrantConnect(PlanCompare, DataStack.AppUser);
        props.Db.GrantConnect(PersistResults, DataStack.AppUser);

        // Ngưỡng pHash và giới hạn gọi Bedrock mỗi biên bản (BR-26): đổi trong SSM, không cần deploy lại
        var thresholdName = $"/roomledger/{props.Stage}/compare/phash-threshold";
        var maxCallsName = $"/roomledger/{props.Stage}/compare/max-calls";
        var threshold = new StringParameter(this, "PhashThreshold", new StringParameterProps
        {
            ParameterName = thresholdName,
            StringValue = "6",
            Description = "BR-26: pHash Hamming distance below this skips Bedrock",
        });
        var maxCalls = new StringParameter(this, "CompareMaxCalls", new StringParameterProps
        {
            ParameterName = maxCallsName,
            StringValue = "30",
            Description = "BR-26: max Bedrock calls per move-out inspection",
        });

        Tasks.CallAwsService ReadSetting(string id, string name, StringParameter parameter, string resultPath) =>
            new(this, id, new Tasks.CallAwsServiceProps
            {
                Service = "ssm",
                Action = "getParameter",
                IamAction = "ssm:GetParameter",
                IamResources = new[] { parameter.ParameterArn },
                Parameters = new Dictionary<string, object> { ["Name"] = name },
                ResultSelector = new Dictionary<string, object> { ["Value.$"] = "States.StringToJson($.Parameter.Value)" },
                ResultPath = resultPath,
            });

        // Đầu vào = detail của MoveOutSubmitted: { inspectionId, tenancyId, moveInInspectionId }
        var plan = new Tasks.LambdaInvoke(this, "Plan", new Tasks.LambdaInvokeProps
        {
            LambdaFunction = PlanCompare,
            Payload = Sfn.TaskInput.FromObject(new Dictionary<string, object>
            {
                ["InspectionId"] = Sfn.JsonPath.StringAt("$.inspectionId"),
                ["MoveInInspectionId"] = Sfn.JsonPath.StringAt("$.moveInInspectionId"),
                ["Threshold"] = Sfn.JsonPath.NumberAt("$.threshold.Value"),
                ["MaxCalls"] = Sfn.JsonPath.NumberAt("$.maxCalls.Value"),
            }),
            PayloadResponseOnly = true,
            ResultPath = "$.plan",
        });

        var compare = new Tasks.LambdaInvoke(this, "ComparePair", new Tasks.LambdaInvokeProps
        {
            LambdaFunction = ComparePairs,
            PayloadResponseOnly = true,
        });
        compare.AddRetry(new Sfn.RetryProps
        {
            Errors = new[] { Sfn.Errors.ALL },
            MaxAttempts = 2,
            Interval = Duration.Seconds(5),
            BackoffRate = 2,
        });
        // Hết lượt thử: cặp này NOT_ANALYZED, các cặp khác vẫn chạy, quyết toán không bị chặn (BR-26 EXCEPTION 2)
        compare.AddCatch(new Sfn.Pass(this, "PairNotAnalyzed", new Sfn.PassProps
        {
            Parameters = new Dictionary<string, object>
            {
                ["MoveOutPhotoId.$"] = "$.MoveOutPhotoId",
                ["Status"] = "NOT_ANALYZED",
            },
        }), new Sfn.CatchProps { Errors = new[] { Sfn.Errors.ALL }, ResultPath = "$.error" });
        var map = new Sfn.Map(this, "CompareEachPair", new Sfn.MapProps
        {
            ItemsPath = "$.plan.ToCompare",
            MaxConcurrency = CompareConcurrency,
            ResultPath = "$.results",
        });
        map.ItemProcessor(compare);

        var persist = new Tasks.LambdaInvoke(this, "Persist", new Tasks.LambdaInvokeProps
        {
            LambdaFunction = PersistResults,
            Payload = Sfn.TaskInput.FromObject(new Dictionary<string, object>
            {
                ["InspectionId"] = Sfn.JsonPath.StringAt("$.plan.InspectionId"),
                ["Pairs"] = Sfn.JsonPath.ListAt("$.plan.Pairs"),
                ["Results"] = Sfn.JsonPath.ListAt("$.results"),
            }),
            PayloadResponseOnly = true,
            ResultPath = "$.persist",
        });
        persist.AddRetry(new Sfn.RetryProps   // ghi một lần (UPDATE có điều kiện) nên thử lại an toàn
        {
            Errors = new[] { Sfn.Errors.ALL },
            MaxAttempts = 3,
            Interval = Duration.Seconds(10),
            BackoffRate = 2,
        });

        Dictionary<string, object> Count(string name, string path) => new()
        {
            ["MetricName"] = name,
            ["Unit"] = "Count",
            ["Value"] = Sfn.JsonPath.NumberAt(path),
            ["Dimensions"] = new object[] { new Dictionary<string, object> { ["Name"] = "Stage", ["Value"] = props.Stage } },
        };
        var metrics = new Tasks.CallAwsService(this, "PutCompareMetrics", new Tasks.CallAwsServiceProps
        {
            Service = "cloudwatch",
            Action = "putMetricData",
            IamAction = "cloudwatch:PutMetricData",
            IamResources = new[] { "*" },   // PutMetricData không hỗ trợ giới hạn theo resource
            Parameters = new Dictionary<string, object>
            {
                ["Namespace"] = "RoomLedger/Compare",
                ["MetricData"] = new object[]
                {
                    Count("PairsAnalyzed", "$.persist.Analyzed"),
                    Count("PairsSkipped", "$.persist.Skipped"),
                    Count("PairsNotAnalyzed", "$.persist.NotAnalyzed"),
                    Count("FallbackUsed", "$.persist.UsedFallback"),
                    Count("FindingsSuggested", "$.persist.Suggested"),
                },
            },
            ResultPath = Sfn.JsonPath.DISCARD,
        });
        var compared = new Tasks.EventBridgePutEvents(this, "PublishMoveOutCompared", new Tasks.EventBridgePutEventsProps
        {
            Entries = new[]
            {
                new Tasks.EventBridgePutEventsEntry
                {
                    EventBus = defaultBus,
                    Source = FunctionsSource,
                    DetailType = MoveOutCompared,
                    Detail = Sfn.TaskInput.FromObject(new Dictionary<string, object>
                    {
                        ["inspectionId"] = Sfn.JsonPath.StringAt("$.persist.InspectionId"),
                        ["tenancyId"] = Sfn.JsonPath.StringAt("$.persist.TenancyId"),
                        ["suggested"] = Sfn.JsonPath.NumberAt("$.persist.Suggested"),
                        ["notAnalyzed"] = Sfn.JsonPath.NumberAt("$.persist.NotAnalyzed"),
                    }),
                },
            },
            ResultPath = Sfn.JsonPath.DISCARD,
        });

        var afterPersist = new Sfn.Choice(this, "Persisted?")
            .When(Sfn.Condition.BooleanEquals("$.persist.Persisted", true), metrics.Next(compared))
            .Otherwise(new Sfn.Succeed(this, "AlreadyPersisted"));
        var definition = ReadSetting("ReadThreshold", thresholdName, threshold, "$.threshold")
            .Next(ReadSetting("ReadMaxCalls", maxCallsName, maxCalls, "$.maxCalls"))
            .Next(plan)
            .Next(new Sfn.Choice(this, "StillComparing?")
                .When(Sfn.Condition.BooleanEquals("$.plan.Proceed", true), map.Next(persist).Next(afterPersist))
                .Otherwise(new Sfn.Succeed(this, "NotComparing")));
        CompareStateMachine = new Sfn.StateMachine(this, "MoveOutCompare", new Sfn.StateMachineProps
        {
            StateMachineName = $"roomledger-{props.Stage}-move-out-compare",
            DefinitionBody = Sfn.DefinitionBody.FromChainable(definition),
            Timeout = Duration.Minutes(30),
        });

        new Rule(this, "MoveOutSubmittedRule", new RuleProps
        {
            Description = "Move-out submitted (API) -> move-out-compare state machine",
            EventPattern = new EventPattern
            {
                Source = new[] { ApiSource },
                DetailType = new[] { MoveOutSubmitted },
            },
            Targets = new IRuleTarget[]
            {
                new SfnStateMachine(CompareStateMachine, new SfnStateMachineProps
                {
                    Input = RuleTargetInput.FromEventPath("$.detail"),
                    RetryAttempts = 2,
                    DeadLetterQueue = DeadLetterQueue,
                }),
            },
        });

        new Alarm(this, "WorkflowDlqNotEmpty", new AlarmProps
        {
            AlarmName = $"roomledger-{props.Stage}-workflow-dlq-not-empty",
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
        new Alarm(this, "MoveOutCompareFailed", new AlarmProps
        {
            AlarmName = $"roomledger-{props.Stage}-move-out-compare-failed",
            AlarmDescription = "A move-out compare run failed; the inspection stays COMPARING",
            Metric = CompareStateMachine.MetricFailed(new MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" }),
            Threshold = 1,
            EvaluationPeriods = 1,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        });

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
            Description = "photo-ingest done -> photo-derive",
            EventPattern = new EventPattern
            {
                Source = new[] { FunctionsSource },
                DetailType = new[] { PhotoReceived },
            },
            Targets = new IRuleTarget[] { new LambdaFunction(PhotoDerive, new LambdaFunctionProps { RetryAttempts = 2, DeadLetterQueue = DeadLetterQueue }) },
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
    }
}
