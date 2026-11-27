using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.SSM;
using Amazon.CDK.AWS.StepFunctions;
using Amazon.CDK.AWS.StepFunctions.Tasks;
using Constructs;
using Errors = Amazon.CDK.AWS.StepFunctions.Errors;
using LogGroup = Amazon.CDK.AWS.Logs.LogGroup;
using LogGroupProps = Amazon.CDK.AWS.Logs.LogGroupProps;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của AiStack. Bucket truyền bằng tên (như WorkflowStack) để các stack không phụ thuộc vòng lẫn nhau.
 * SesSender và WebOrigin cùng có hoặc cùng không: không có SesSender thì không dựng phần gửi email.
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed class AiStackProps : StackProps
{
    public required string Stage { get; init; }
    public required IVpc Vpc { get; init; }
    public required ISecurityGroup LambdaSg { get; init; }
    public required IDatabaseInstance Db { get; init; }
    public required string FunctionsAssetPath { get; init; }
    public required string IncomingBucketName { get; init; }
    public required string OutboxBucketName { get; init; }
    /** User Pool người dùng: Lambda notify tra email theo sub. */
    public required IUserPool Users { get; init; }
    /** Prompt tiếng Việt của hai tác vụ, nạp vào Parameter Store lúc deploy (file ở src/backend/src/RoomLedger.Ai/Prompts). */
    public required string ElectricPrompt { get; init; }
    public required string WaterPrompt { get; init; }
    /** Id mô hình hoặc inference profile (au.*); đổi giá trị thì policy Bedrock cũng phải khớp (xem BedrockProfilePrefix). */
    public string ModelId { get; init; } = AiStack.DefaultModelId;
    /** Identity SES đã xác minh dùng làm người gửi (đặt qua -c sesSender=..., không ghi trong code). */
    public string? SesSender { get; init; }
    /** Origin của web app, dùng cho đường dẫn trong email (bắt buộc khi có SesSender). */
    public string? WebOrigin { get; init; }
    /** Topic nhận báo động (ObservabilityStack); null → báo động không gửi đi đâu. */
    public Amazon.CDK.AWS.SNS.ITopic? AlarmTopic { get; init; }
}

/**
 * AI đọc đồng hồ điện nước và thông báo (sơ đồ final, luồng AI và email).
 *
 * Đọc đồng hồ: ảnh tải lên bucket incoming → EventBridge → state machine (Step Functions Standard):
 * meter-prepare (VPC: ảnh có phải ảnh đồng hồ đang chờ AI) → read-document (NGOÀI VPC: thu nhỏ ảnh, gọi Bedrock
 * Claude Haiku 4.5 qua inference profile au.*, thử lại 2 lần) → meter-persist (VPC: ghi RDS theo BR-20, báo chủ nhà khi
 * cần kiểm tra). Bedrock lỗi sau khi thử lại cũng đi tới meter-persist với lỗi, chỉ số thành NEEDS_REVIEW để chủ nhà nhập tay.
 * Id mô hình và prompt ở Parameter Store (Standard, miễn phí): /roomledger/{stage}/ai/model-id, /ai/prompt/{tác vụ};
 * sửa giá trị trong console có hiệu lực sau tối đa 5 phút, không cần deploy (deploy lại nạp lại từ file prompt).
 * Lambda read-document không vào VPC (không có endpoint Bedrock, và không chạm RDS), chỉ có quyền đọc ảnh incoming,
 * đọc 3 tham số trên và InvokeModel đúng profile Haiku 4.5.
 *
 * Thông báo: sự kiện nghiệp vụ trong bucket outbox (rule theo tiền tố events/{source}/{loại}/) → Lambda notify
 * (ngoài VPC) → SES. Lỗi gửi thử lại 2 lần rồi vào SQS DLQ, có báo động. SES mới ở chế độ sandbox: chỉ gửi được tới
 * địa chỉ đã xác minh (xem docs hướng dẫn); stack không tạo identity và không ghi địa chỉ email nào.
 *
 * Phụ trách: Sang (đọc đồng hồ), Tuấn (viết code).
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed class AiStack : Stack
{
    /** Inference profile vùng Úc của Claude Haiku 4.5 (cross-region trong ap-southeast-2/4, dữ liệu ở lại nước Úc). */
    public const string DefaultModelId = "au.anthropic.claude-haiku-4-5-20251001-v1:0";
    /** Policy Bedrock cho mọi bản Haiku 4.5 của profile au.*; đổi sang họ mô hình khác thì sửa tiền tố này. */
    public const string BedrockProfilePrefix = "au.anthropic.claude-haiku-4-5-";
    public const string FoundationModelPrefix = "anthropic.claude-haiku-4-5-";

    public static string ParameterPrefix(string stage) => $"/{Naming.Project}/{stage}";

    /** Tiền tố khóa outbox (events/{source}/{loại}/) của các sự kiện có mẫu email. */
    public static IReadOnlyList<string> NotifyKeyPrefixes() =>
    [
        .. new[] { "InviteCreated", "TenancyMoved", "InvoiceSent", "InvoiceDisputed", "FindingsSent", "FindingDisputed", "DisputeAnswered" }
            .Select(t => $"events/{WorkflowStack.ApiSource}/{t}/"),
        $"events/{WorkflowStack.FunctionsSource}/MeterReadingNeedsReview/",
    ];

    public Function ReadDocument { get; }
    public Function MeterPrepare { get; }
    public Function MeterPersist { get; }
    public Function? Notify { get; }
    public StateMachine ReadMeterMachine { get; }
    public Queue DeadLetterQueue { get; }

    public AiStack(Construct scope, string id, AiStackProps props) : base(scope, id, props)
    {
        if (props.SesSender is { Length: > 0 } && props.WebOrigin is not { Length: > 0 })
        {
            throw new InvalidOperationException("Có sesSender thì phải có webOrigin: email cần đường dẫn về web app");
        }

        var stage = props.Stage;
        var code = Code.FromAsset(props.FunctionsAssetPath);
        var prefix = ParameterPrefix(stage);
        string Name(string n) => Naming.Resource(stage, n);
        string FunctionHandler(string handlerClass, string method) => $"RoomLedger.Functions::RoomLedger.Functions.{handlerClass}.Function::{method}";
        string Outbox(string path) => $"arn:{Aws.PARTITION}:s3:::{props.OutboxBucketName}/events/{path}";

        ILogGroup Logs(string logicalId, string shortName) => new LogGroup(this, logicalId, new LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{Name(shortName)}",
            Retention = WorkflowStack.LogRetentionFor(stage),
            RemovalPolicy = stage == "prod" ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY,
        });

        // ---- Parameter Store (Standard): id mô hình và prompt ----
        var modelParam = new StringParameter(this, "ModelIdParam", new StringParameterProps
        {
            ParameterName = $"{prefix}/ai/model-id",
            StringValue = props.ModelId,
            Tier = ParameterTier.STANDARD,
            Description = "Bedrock model or inference profile id used to read meters",
        });
        var electricParam = new StringParameter(this, "ElectricPromptParam", new StringParameterProps
        {
            ParameterName = $"{prefix}/ai/prompt/readElectricMeter",
            StringValue = props.ElectricPrompt,
            Tier = ParameterTier.STANDARD,
            Description = "Vietnamese prompt: read an electricity meter photo",
        });
        var waterParam = new StringParameter(this, "WaterPromptParam", new StringParameterProps
        {
            ParameterName = $"{prefix}/ai/prompt/readWaterMeter",
            StringValue = props.WaterPrompt,
            Tier = ParameterTier.STANDARD,
            Description = "Vietnamese prompt: read a water meter photo",
        });

        // Sự kiện không giao được (EventBridge → state machine, Lambda notify hết lần thử) vào đây
        DeadLetterQueue = new Queue(this, "AiDlq", new QueueProps
        {
            QueueName = Name("ai-dlq"),
            RetentionPeriod = Duration.Days(14),
            Encryption = QueueEncryption.SQS_MANAGED,
        });

        // ---- Lambda ngoài VPC: read-document (Bedrock) ----
        ReadDocument = new Function(this, "ReadDocument", new FunctionProps
        {
            FunctionName = Name("read-document"),
            LogGroup = Logs("ReadDocumentLogs", "read-document"),
            Runtime = Runtime.DOTNET_8,
            Architecture = Architecture.ARM_64,
            Handler = FunctionHandler("ReadDocument", "HandleAsync"),
            Code = code,
            MemorySize = 1536,   // Magick.NET giải mã ảnh điện thoại (HEIC 12 MP ≈ 50 MB điểm ảnh)
            Timeout = Duration.Seconds(60),
            Environment = new Dictionary<string, string>
            {
                ["INCOMING_BUCKET"] = props.IncomingBucketName,
                ["AI_PARAM_PREFIX"] = prefix,
            },
        });
        ReadDocument.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:GetObject" },
            Resources = new[] { $"arn:{Aws.PARTITION}:s3:::{props.IncomingBucketName}/*" },
        }));
        foreach (var parameter in new[] { modelParam, electricParam, waterParam })
        {
            parameter.GrantRead(ReadDocument);
        }
        // Converse cần bedrock:InvokeModel: trên inference profile của tài khoản và trên mô hình gốc, nhưng mô hình gốc
        // chỉ được dùng khi đi qua đúng profile đó (điều kiện bedrock:InferenceProfileArn).
        var profileArn = $"arn:{Aws.PARTITION}:bedrock:{Aws.REGION}:{Aws.ACCOUNT_ID}:inference-profile/{BedrockProfilePrefix}*";
        ReadDocument.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "bedrock:InvokeModel" },
            Resources = new[] { profileArn },
        }));
        ReadDocument.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "bedrock:InvokeModel" },
            Resources = new[] { $"arn:{Aws.PARTITION}:bedrock:*::foundation-model/{FoundationModelPrefix}*" },
            Conditions = new Dictionary<string, object>
            {
                ["StringLike"] = new Dictionary<string, object> { ["bedrock:InferenceProfileArn"] = profileArn },
            },
        }));

        // ---- Lambda trong VPC: meter-prepare, meter-persist (đọc/ghi RDS bằng IAM auth, như WorkflowStack) ----
        var vpcEnvironment = new Dictionary<string, string>
        {
            ["ROOMLEDGER_DB"] = $"Host={props.Db.DbInstanceEndpointAddress};Port={DataStack.DbPort};Database={DataStack.DbName};" +
                                $"Username={DataStack.AppUser};SSL Mode=Require",
            ["OUTBOX_BUCKET"] = props.OutboxBucketName,
        };
        Function VpcFunction(string logicalId, string shortName, string method) => new(this, logicalId, new FunctionProps
        {
            FunctionName = Name(shortName),
            LogGroup = Logs($"{logicalId}Logs", shortName),
            Runtime = Runtime.DOTNET_8,
            Architecture = Architecture.ARM_64,
            Handler = FunctionHandler("MeterResult", method),
            Code = code,
            MemorySize = 512,
            Timeout = Duration.Seconds(30),
            Vpc = props.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = new[] { props.LambdaSg },
            Environment = vpcEnvironment,
        });
        MeterPrepare = VpcFunction("MeterPrepare", "meter-prepare", "PrepareAsync");
        MeterPersist = VpcFunction("MeterPersist", "meter-persist", "PersistAsync");
        props.Db.GrantConnect(MeterPrepare, DataStack.AppUser);
        props.Db.GrantConnect(MeterPersist, DataStack.AppUser);
        // meter-persist phát MeterReadingNeedsReview vào outbox (qua S3 Gateway endpoint), chỉ đúng loại sự kiện này
        MeterPersist.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = new[] { "s3:PutObject" },
            Resources = new[] { Outbox($"{WorkflowStack.FunctionsSource}/MeterReadingNeedsReview/*") },
        }));

        // ---- State machine ----
        var prepare = new LambdaInvoke(this, "PrepareMeterPhoto", new LambdaInvokeProps
        {
            StateName = "Is it a meter photo awaiting AI",
            LambdaFunction = MeterPrepare,
            PayloadResponseOnly = true,
            ResultPath = "$.prepare",
        });
        var read = new LambdaInvoke(this, "ReadMeterPhoto", new LambdaInvokeProps
        {
            StateName = "Read the meter with Bedrock",
            LambdaFunction = ReadDocument,
            Payload = TaskInput.FromObject(new Dictionary<string, object>
            {
                ["readingId"] = JsonPath.StringAt("$.prepare.readingId"),
                ["key"] = JsonPath.StringAt("$.prepare.key"),
                ["task"] = JsonPath.StringAt("$.prepare.task"),
            }),
            PayloadResponseOnly = true,
            ResultPath = "$.result",
            TaskTimeout = Amazon.CDK.AWS.StepFunctions.Timeout.Duration(Duration.Seconds(90)),
        });
        read.AddRetry(new RetryProps { Errors = new[] { Errors.ALL }, MaxAttempts = 2, Interval = Duration.Seconds(5), BackoffRate = 2 });
        var persistResult = new LambdaInvoke(this, "PersistReading", new LambdaInvokeProps
        {
            StateName = "Save the reading",
            LambdaFunction = MeterPersist,
            Payload = TaskInput.FromObject(new Dictionary<string, object>
            {
                ["readingId"] = JsonPath.StringAt("$.result.readingId"),
                ["key"] = JsonPath.StringAt("$.result.key"),
                ["readable"] = JsonPath.StringAt("$.result.readable"),   // đường dẫn tới giá trị boolean cũng dùng được
                ["value"] = JsonPath.NumberAt("$.result.value"),
                ["confidence"] = JsonPath.NumberAt("$.result.confidence"),
                ["note"] = JsonPath.StringAt("$.result.note"),
            }),
            PayloadResponseOnly = true,
            ResultPath = JsonPath.DISCARD,
        });
        var persistError = new LambdaInvoke(this, "PersistFailure", new LambdaInvokeProps
        {
            StateName = "Save the failure for manual entry",
            LambdaFunction = MeterPersist,
            Payload = TaskInput.FromObject(new Dictionary<string, object>
            {
                ["readingId"] = JsonPath.StringAt("$.prepare.readingId"),
                ["key"] = JsonPath.StringAt("$.prepare.key"),
                ["error"] = "AI could not read the photo",   // không chuyển Cause của lỗi sang DB
            }),
            PayloadResponseOnly = true,
            ResultPath = JsonPath.DISCARD,
        });
        persistResult.AddRetry(new RetryProps { Errors = new[] { Errors.ALL }, MaxAttempts = 3, Interval = Duration.Seconds(3), BackoffRate = 2 });
        persistError.AddRetry(new RetryProps { Errors = new[] { Errors.ALL }, MaxAttempts = 3, Interval = Duration.Seconds(3), BackoffRate = 2 });
        read.AddCatch(persistError, new CatchProps { Errors = new[] { Errors.ALL }, ResultPath = "$.error" });
        read.Next(persistResult);
        var definition = prepare.Next(new Choice(this, "IsMeterPhoto", new ChoiceProps { StateName = "Found" })
            .When(Condition.BooleanEquals("$.prepare.found", true), read)
            .Otherwise(new Succeed(this, "NotMeterPhoto", new SucceedProps { StateName = "Ignore" })));

        var machineLogs = new LogGroup(this, "ReadMeterMachineLogs", new LogGroupProps
        {
            LogGroupName = $"/aws/vendedlogs/states/{Name("read-meter")}",
            Retention = WorkflowStack.LogRetentionFor(stage),
            RemovalPolicy = stage == "prod" ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY,
        });
        ReadMeterMachine = new StateMachine(this, "ReadMeterMachine", new StateMachineProps
        {
            StateMachineName = Name("read-meter"),
            StateMachineType = StateMachineType.STANDARD,
            DefinitionBody = DefinitionBody.FromChainable(definition),
            Timeout = Duration.Minutes(10),
            Logs = new LogOptions { Destination = machineLogs, Level = LogLevel.ERROR },
        });

        new Rule(this, "MeterPhotoUploaded", new RuleProps
        {
            Description = "New object in the incoming bucket -> read-meter state machine (ignores photos that are not meter photos)",
            EventPattern = new EventPattern
            {
                Source = new[] { "aws.s3" },
                DetailType = new[] { "Object Created" },
                Detail = new Dictionary<string, object>
                {
                    ["bucket"] = new Dictionary<string, object> { ["name"] = new[] { props.IncomingBucketName } },
                },
            },
            Targets = new IRuleTarget[] { new SfnStateMachine(ReadMeterMachine, new SfnStateMachineProps { RetryAttempts = 2, DeadLetterQueue = DeadLetterQueue }) },
        });

        // ---- Thông báo: outbox → notify (ngoài VPC) → SES ----
        if (props.SesSender is { Length: > 0 } sender)
        {
            var notifyEnvironment = new Dictionary<string, string>
            {
                ["OUTBOX_BUCKET"] = props.OutboxBucketName,
                ["SES_SENDER"] = sender,
                ["WEB_ORIGIN"] = props.WebOrigin!,
                ["USER_POOL_ID"] = props.Users.UserPoolId,
            };
            Notify = new Function(this, "Notify", new FunctionProps
            {
                FunctionName = Name("notify"),
                LogGroup = Logs("NotifyLogs", "notify"),
                Runtime = Runtime.DOTNET_8,
                Architecture = Architecture.ARM_64,
                Handler = FunctionHandler("Notify", "HandleAsync"),
                Code = code,
                MemorySize = 256,
                Timeout = Duration.Seconds(30),
                Environment = notifyEnvironment,
                RetryAttempts = 2,
                DeadLetterQueue = DeadLetterQueue,
            });
            Notify.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "s3:GetObject" },
                Resources = NotifyKeyPrefixes().Select(p => $"arn:{Aws.PARTITION}:s3:::{props.OutboxBucketName}/{p}*").ToArray(),
            }));
            // Chỉ gửi từ đúng địa chỉ đã cấu hình (identity có thể là địa chỉ hoặc cả tên miền)
            Notify.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "ses:SendEmail" },
                Resources = new[] { $"arn:{Aws.PARTITION}:ses:{Aws.REGION}:{Aws.ACCOUNT_ID}:identity/*" },
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object> { ["ses:FromAddress"] = sender },
                },
            }));
            Notify.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "cognito-idp:ListUsers" },
                Resources = new[] { props.Users.UserPoolArn },
            }));

            new Rule(this, "NotifyOnEvents", new RuleProps
            {
                Description = "Business events written to the outbox bucket -> notify (email through SES)",
                EventPattern = new EventPattern
                {
                    Source = new[] { "aws.s3" },
                    DetailType = new[] { "Object Created" },
                    Detail = new Dictionary<string, object>
                    {
                        ["bucket"] = new Dictionary<string, object> { ["name"] = new[] { props.OutboxBucketName } },
                        ["object"] = new Dictionary<string, object>
                        {
                            ["key"] = NotifyKeyPrefixes().Select(p => (object)new Dictionary<string, object> { ["prefix"] = p }).ToArray(),
                        },
                    },
                },
                Targets = new IRuleTarget[] { new LambdaFunction(Notify, new LambdaFunctionProps { RetryAttempts = 2, DeadLetterQueue = DeadLetterQueue }) },
            });
        }

        // ---- Báo động ----
        void Alarm(string logicalId, string name, string description, IMetric metric)
        {
            var alarm = new Alarm(this, logicalId, new AlarmProps
            {
                AlarmName = Name(name),
                AlarmDescription = description,
                Metric = metric,
                Threshold = 1,
                EvaluationPeriods = 1,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
                TreatMissingData = TreatMissingData.NOT_BREACHING,
            });
            if (props.AlarmTopic is not null)
            {
                alarm.AddAlarmAction(new SnsAction(props.AlarmTopic));
            }
        }

        Alarm("AiDlqNotEmpty", "ai-dlq-not-empty", "An event could not be delivered to the read-meter state machine or the notify Lambda",
            DeadLetterQueue.MetricApproximateNumberOfMessagesVisible(new MetricOptions { Period = Duration.Minutes(5), Statistic = "Maximum" }));
        Alarm("ReadMeterFailed", "read-meter-failed", "A read-meter execution failed (the reading may be stuck in PENDING_AI)",
            ReadMeterMachine.MetricFailed(new MetricOptions { Period = Duration.Minutes(5), Statistic = "Sum" }));
    }
}
