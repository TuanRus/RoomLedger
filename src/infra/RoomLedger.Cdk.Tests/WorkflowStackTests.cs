using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.Logs;
using RoomLedger.Cdk.Stacks;
using Xunit;

namespace RoomLedger.Cdk.Tests;

/**
 * WorkflowStack phần upload (T12): photo-ingest và photo-derive trong VPC, nối bằng EventBridge.
 * S3 "Object Created" (bucket incoming) → photo-ingest → PhotoReceived → photo-derive.
 *
 * @author Sang
 * @version 1.2
 */
public class WorkflowStackTests
{
    private const string Ingest = "roomledger-dev-photo-ingest";
    private const string Derive = "roomledger-dev-photo-derive";
    private const string SealFn = "roomledger-dev-evidence-seal";
    private const string RenderFn = "roomledger-dev-render-pdf";

    private static readonly Lazy<Template> DevTemplate = new(Build);

    private static Template Build()
    {
        var app = new App();
        var env = new Amazon.CDK.Environment { Account = "123456789012", Region = "ap-southeast-2" };
        var network = new NetworkStack(app, "TestNetwork", new NetworkStackProps { Stage = "dev", Env = env });

        var asset = Path.Combine(Path.GetTempPath(), "roomledger-test-functions");
        Directory.CreateDirectory(asset);
        File.WriteAllText(Path.Combine(asset, "placeholder.txt"), "test");

        var data = new DataStack(app, "TestData", new DataStackProps
        {
            Stage = "dev", Env = env, Vpc = network.Vpc, LambdaSg = network.LambdaSg, FunctionsAssetPath = asset,
        });
        var observability = new ObservabilityStack(app, "TestObservability", new ObservabilityStackProps { Stage = "dev", Env = env });
        var workflow = new WorkflowStack(app, "TestWorkflow", new WorkflowStackProps
        {
            AlarmTopic = observability.AlarmTopic,
            Stage = "dev",
            Env = env,
            Vpc = network.Vpc,
            LambdaSg = network.LambdaSg,
            Db = data.Db,
            FunctionsAssetPath = asset,
            IncomingBucketName = "roomledger-dev-incoming",
            DerivedBucketName = "roomledger-dev-derived",
            EvidenceBucketName = "roomledger-dev-evidence",
        });
        return Template.FromStack(workflow);
    }

    private static Template Synth() => DevTemplate.Value;

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static IDictionary<string, object> PropsOf(IDictionary<string, object> r) => (IDictionary<string, object>)r["Properties"];

    private static (string LogicalId, IDictionary<string, object> Props) Fn(string name) =>
        Synth().FindResources("AWS::Lambda::Function")
            .Select(kv => (kv.Key, PropsOf(kv.Value)))
            .Single(f => f.Item2.TryGetValue("FunctionName", out var n) && n as string == name);

    private static string PolicyOf(string functionLogicalId) =>
        Json(Synth().FindResources("AWS::IAM::Policy").Values.Select(PropsOf)
            .Where(p => Json(p["Roles"]).Contains(functionLogicalId[..^8])) // ...ServiceRoleXXXX khớp tiền tố id hàm
            .ToList());

    [Theory]
    [InlineData(Ingest, "RoomLedger.Functions::RoomLedger.Functions.PhotoIngest.Function::HandleAsync")]
    [InlineData(Derive, "RoomLedger.Functions::RoomLedger.Functions.PhotoDerive.Function::HandleQueueAsync")]
    [InlineData(SealFn, "RoomLedger.Functions::RoomLedger.Functions.EvidenceSeal.Function::HandleAsync")]
    [InlineData(RenderFn, "RoomLedger.Functions::RoomLedger.Functions.RenderPdf.Function::HandleAsync")]
    public void Functions_RunDotnet8_WithTheRightHandler_InsideTheVpc(string name, string handler)
    {
        var fn = Fn(name).Props;
        Assert.Equal("dotnet8", fn["Runtime"]);
        Assert.Equal(handler, fn["Handler"]);
        var vpc = (IDictionary<string, object>)fn["VpcConfig"];
        Assert.Equal(2, ((object[])vpc["SubnetIds"]).Length);
        Assert.Contains("LambdaSg", Json(vpc["SecurityGroupIds"]));
        Assert.False(fn.ContainsKey("ReservedConcurrentExecutions")); // giới hạn tài khoản là 10
    }

    [Fact]
    public void Derive_HasEnoughMemoryForTwelveMegapixelHeic()
        => Assert.True(Convert.ToInt32(Fn(Derive).Props["MemorySize"]) >= 1536);

    [Fact]
    public void Functions_ConnectAsAppUserWithIamAuth_NoPasswordInEnvironment()
    {
        foreach (var name in new[] { Ingest, Derive })
        {
            var env = (IDictionary<string, object>)((IDictionary<string, object>)Fn(name).Props["Environment"])["Variables"];
            var cs = Json(env["ROOMLEDGER_DB"]);
            Assert.Contains("Username=roomledger_app", cs);
            Assert.DoesNotContain("Password", cs, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("roomledger-dev-incoming", env["INCOMING_BUCKET"]);
            Assert.Equal("roomledger-dev-derived", env["DERIVED_BUCKET"]);
        }
        var policies = Json(Synth().FindResources("AWS::IAM::Policy"));
        Assert.Contains("rds-db:connect", policies);
        Assert.Contains("/roomledger_app", policies);
    }

    [Fact]
    public void ObjectCreatedInIncoming_TriggersIngest()
        => Synth().HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
        {
            ["EventPattern"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["source"] = new[] { "aws.s3" },
                ["detail-type"] = new[] { "Object Created" },
                ["detail"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["bucket"] = new Dictionary<string, object> { ["name"] = new[] { "roomledger-dev-incoming" } },
                }),
            }),
            ["Targets"] = Match.ArrayWith([Match.ObjectLike(new Dictionary<string, object>
            {
                ["Arn"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Fn::GetAtt"] = Match.ArrayWith([Fn(Ingest).LogicalId]),
                }),
            })]),
        });

    [Fact]
    public void PhotoReceived_GoesToThePhotoQueue()
        => Synth().HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
        {
            ["EventPattern"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["source"] = new[] { "roomledger.functions" },
                ["detail-type"] = new[] { "PhotoReceived" },
            }),
            ["Targets"] = Match.ArrayWith([Match.ObjectLike(new Dictionary<string, object>
            {
                ["Arn"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Fn::GetAtt"] = Match.ArrayWith([QueueId("roomledger-dev-photo-queue"), "Arn"]),
                }),
            })]),
        });

    private static string QueueId(string name) =>
        Synth().FindResources("AWS::SQS::Queue").Single(r => (string)PropsOf(r.Value)["QueueName"] == name).Key;

    [Fact]
    public void PhotoQueue_RedrivesToTheDlqAfter3Tries_AndOutlastsDeriveTimeout()
        => Synth().HasResourceProperties("AWS::SQS::Queue", new Dictionary<string, object>
        {
            ["QueueName"] = "roomledger-dev-photo-queue",
            ["VisibilityTimeout"] = 720,
            ["SqsManagedSseEnabled"] = true,
            ["RedrivePolicy"] = new Dictionary<string, object>
            {
                ["deadLetterTargetArn"] = new Dictionary<string, object> { ["Fn::GetAtt"] = new[] { QueueId("roomledger-dev-workflow-dlq"), "Arn" } },
                ["maxReceiveCount"] = 3,
            },
        });

    [Fact]
    public void PhotoQueue_FeedsDerive_WithPartialBatchFailures_AndCappedConcurrency()
        => Synth().HasResourceProperties("AWS::Lambda::EventSourceMapping", new Dictionary<string, object>
        {
            ["FunctionName"] = new Dictionary<string, object> { ["Ref"] = Fn(Derive).LogicalId },
            ["EventSourceArn"] = new Dictionary<string, object> { ["Fn::GetAtt"] = new[] { QueueId("roomledger-dev-photo-queue"), "Arn" } },
            ["BatchSize"] = 5,
            ["FunctionResponseTypes"] = new[] { "ReportBatchItemFailures" },
            ["ScalingConfig"] = new Dictionary<string, object> { ["MaximumConcurrency"] = 2 },
        });

    [Fact]
    public void Events_CanSendToThePhotoQueue()
        => Synth().HasResourceProperties("AWS::SQS::QueuePolicy", new Dictionary<string, object>
        {
            ["Queues"] = Match.ArrayWith([new Dictionary<string, object> { ["Ref"] = QueueId("roomledger-dev-photo-queue") }]),
            ["PolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith([Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Action"] = Match.ArrayWith(["sqs:SendMessage"]),
                    ["Condition"] = Match.ObjectLike(new Dictionary<string, object> { ["ArnEquals"] = Match.AnyValue() }),
                    ["Principal"] = new Dictionary<string, object> { ["Service"] = "events.amazonaws.com" },
                })]),
            }),
        });

    [Fact]
    public void Ingest_OnlyReadsIncoming_AndPublishesEvents()
    {
        var policy = PolicyOf(Fn(Ingest).LogicalId);
        Assert.Contains("s3:GetObject", policy);
        Assert.Contains("roomledger-dev-incoming/*", policy);
        Assert.DoesNotContain("s3:PutObject", policy);
        Assert.DoesNotContain("roomledger-dev-derived", policy);
        Assert.Contains("events:PutEvents", policy);
    }

    [Fact]
    public void Derive_ReadsIncoming_WritesOnlyDerived_AndPublishesNothing()
    {
        var policy = PolicyOf(Fn(Derive).LogicalId);
        Assert.Contains("s3:GetObject", policy);
        Assert.Contains("s3:PutObject", policy);
        Assert.Contains("roomledger-dev-derived/*", policy);
        Assert.DoesNotContain("events:PutEvents", policy);
        Assert.DoesNotContain("s3:DeleteObject", policy);
    }

    [Fact]
    public void InspectionLockedFromTheApi_TriggersEvidenceSeal()
        => Synth().HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
        {
            ["EventPattern"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["source"] = new[] { "roomledger.api" },
                ["detail-type"] = new[] { "InspectionLocked" },
            }),
            ["Targets"] = Match.ArrayWith([Match.ObjectLike(new Dictionary<string, object>
            {
                ["Arn"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Fn::GetAtt"] = Match.ArrayWith([Fn(SealFn).LogicalId]),
                }),
            })]),
        });

    /** MD-06-FE-05: quyết toán xong → biên bản thanh lý PDF và niêm phong ảnh trả phòng (evidence-seal nhận inspectionId). */
    [Fact]
    public void TenancySettledFromTheApi_TriggersRenderPdf_AndSealsTheMoveOutPhotos()
    {
        var rule = Synth().FindResources("AWS::Events::Rule").Values.Select(PropsOf)
            .Single(r => Json(r["EventPattern"]).Contains("\"TenancySettled\""));
        Assert.Contains("roomledger.api", Json(rule["EventPattern"]));
        var targets = Json(rule["Targets"]);
        Assert.Contains(Fn(RenderFn).LogicalId, targets);
        Assert.Contains(Fn(SealFn).LogicalId, targets);
        Assert.Contains("DeadLetterConfig", targets);
    }

    [Fact]
    public void RenderPdf_WritesOnlyDerivedReports_ReadsTheDatabase_NoEvidenceNoDeleteNoBedrock()
    {
        var policy = PolicyOf(Fn(RenderFn).LogicalId);
        Assert.Contains("s3:PutObject", policy);
        Assert.Contains("roomledger-dev-derived/reports/*", policy);
        Assert.DoesNotContain("roomledger-dev-evidence", policy);
        Assert.DoesNotContain("roomledger-dev-incoming", policy);
        Assert.DoesNotContain("s3:DeleteObject", policy);
        Assert.DoesNotContain("bedrock", policy);
        Assert.Contains("rds-db:connect", policy);
    }

    /** Báo động của WorkflowStack gửi vào topic chung của ObservabilityStack (người nhận do Phát chọn). */
    [Theory]
    [InlineData("roomledger-dev-workflow-dlq-not-empty")]
    public void WorkflowAlarms_NotifyTheSharedAlarmTopic(string alarmName)
    {
        var alarm = Synth().FindResources("AWS::CloudWatch::Alarm").Values.Select(PropsOf)
            .Single(a => a["AlarmName"] as string == alarmName);
        Assert.Contains("ImportValue", Json(alarm["AlarmActions"]));   // tham chiếu topic của stack observability
        Assert.Contains("AlarmTopic", Json(alarm["AlarmActions"]));
    }

    /** Log của mọi Lambda trong WorkflowStack có hạn giữ (dev 30 ngày), không giữ mãi mãi. */
    [Fact]
    public void EveryWorkflowLambda_LogsToAGroupWithRetention()
    {
        var names = new[] { Ingest, Derive, SealFn, RenderFn };
        var groups = Synth().FindResources("AWS::Logs::LogGroup").Values.Select(PropsOf).ToList();
        Assert.Equal(names.Length, groups.Count);
        foreach (var name in names)
        {
            var group = groups.Single(g => g["LogGroupName"] as string == $"/aws/lambda/{name}");
            Assert.Equal(30d, Convert.ToDouble(group["RetentionInDays"]));
            Assert.True(Fn(name).Props.ContainsKey("LoggingConfig"), name);
        }
    }

    [Theory]
    [InlineData("dev", RetentionDays.ONE_MONTH)]
    [InlineData("demo", RetentionDays.ONE_MONTH)]
    [InlineData("prod", RetentionDays.THREE_MONTHS)]
    public void LogRetention_IsAMonthOutsideProd_ThreeMonthsInProd(string stage, RetentionDays expected)
    {
        Assert.Equal(expected, WorkflowStack.LogRetentionFor(stage));
    }

    [Fact]
    public void Seal_ReadsIncoming_WritesAndReadsEvidence_CannotDeleteOrChangeRetention()
    {
        var policy = PolicyOf(Fn(SealFn).LogicalId);
        Assert.Contains("roomledger-dev-incoming/*", policy);
        Assert.Contains("roomledger-dev-evidence/*", policy);
        Assert.Contains("s3:GetObject", policy);
        Assert.Contains("s3:PutObject", policy);
        Assert.DoesNotContain("s3:DeleteObject", policy);
        Assert.DoesNotContain("BypassGovernanceRetention", policy);
        Assert.DoesNotContain("PutObjectRetention", policy);
        Assert.DoesNotContain("roomledger-dev-derived", policy);
        var env = (IDictionary<string, object>)((IDictionary<string, object>)Fn(SealFn).Props["Environment"])["Variables"];
        Assert.Equal("roomledger-dev-evidence", env["EVIDENCE_BUCKET"]);
    }

    [Fact]
    public void NoPolicyAllowsEveryAction()
        => Assert.DoesNotContain("\"Action\":\"*\"", Json(Synth().FindResources("AWS::IAM::Policy")));

    // Đã bỏ AI so ảnh trả phòng (kiến trúc v8.5.2): không còn state machine, Lambda so ảnh hay quyền Bedrock.

    [Fact]
    public void NoCompareStateMachine_NoBedrockPermission_NoMoveOutSubmittedRule()
    {
        Synth().ResourceCountIs("AWS::StepFunctions::StateMachine", 0);
        Assert.DoesNotContain("bedrock", Json(Synth().FindResources("AWS::IAM::Policy")));
        Assert.DoesNotContain("MoveOutSubmitted", Json(Synth().FindResources("AWS::Events::Rule")));
        Assert.Empty(Synth().FindResources("AWS::SSM::Parameter"));
    }

    [Fact]
    public void EveryRuleTarget_HasADeadLetterQueue()
    {
        var rules = Synth().FindResources("AWS::Events::Rule").Values.Select(PropsOf).ToList();
        Assert.Equal(4, rules.Count);   // PhotoUploaded, PhotoReceived, InspectionLocked, TenancySettled
        foreach (var target in rules.SelectMany(r => (object[])r["Targets"]).Cast<IDictionary<string, object>>())
        {
            Assert.True(target.ContainsKey("DeadLetterConfig"), Json(target));
        }
    }

    [Fact]
    public void Alarms_OnTheDeadLetterQueue()
    {
        Synth().HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
        {
            ["MetricName"] = "ApproximateNumberOfMessagesVisible",
            ["Namespace"] = "AWS/SQS",
            ["Threshold"] = 1,
            ["ComparisonOperator"] = "GreaterThanOrEqualToThreshold",
        });
    }
}
