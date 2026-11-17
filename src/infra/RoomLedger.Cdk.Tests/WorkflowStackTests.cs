using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using RoomLedger.Cdk.Stacks;
using Xunit;

namespace RoomLedger.Cdk.Tests;

/**
 * WorkflowStack phần upload (T12): photo-ingest và photo-derive trong VPC, nối bằng EventBridge.
 * S3 "Object Created" (bucket incoming) → photo-ingest → PhotoReceived → photo-derive.
 *
 * @author Sang
 * @version 1.1
 */
public class WorkflowStackTests
{
    private const string Ingest = "roomledger-dev-photo-ingest";
    private const string Derive = "roomledger-dev-photo-derive";
    private const string SealFn = "roomledger-dev-evidence-seal";
    private const string PlanFn = "roomledger-dev-plan-compare";
    private const string PairsFn = "roomledger-dev-compare-pairs";
    private const string PersistFn = "roomledger-dev-persist-results";
    private const string RenderFn = "roomledger-dev-render-pdf";
    private const string Sonnet = "au.anthropic.claude-sonnet-4-5-20250929-v1:0";
    private const string Haiku = "au.anthropic.claude-haiku-4-5-20251001-v1:0";

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
        var workflow = new WorkflowStack(app, "TestWorkflow", new WorkflowStackProps
        {
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
    [InlineData(Derive, "RoomLedger.Functions::RoomLedger.Functions.PhotoDerive.Function::HandleAsync")]
    [InlineData(SealFn, "RoomLedger.Functions::RoomLedger.Functions.EvidenceSeal.Function::HandleAsync")]
    [InlineData(PlanFn, "RoomLedger.Functions::RoomLedger.Functions.PlanCompare.Function::HandleAsync")]
    [InlineData(PersistFn, "RoomLedger.Functions::RoomLedger.Functions.PersistResults.Function::HandleAsync")]
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
    public void PhotoReceived_TriggersDerive()
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
                    ["Fn::GetAtt"] = Match.ArrayWith([Fn(Derive).LogicalId]),
                }),
            })]),
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

    // ---- MD-06-FE-02 slice 3: Step Functions so ảnh trả phòng (Sang) ----

    private static string Definition()
    {
        var sm = PropsOf(Synth().FindResources("AWS::StepFunctions::StateMachine").Values.Single());
        var def = sm["DefinitionString"];
        if (def is string text)
        {
            return text;
        }
        var join = (object[])((IDictionary<string, object>)def)["Fn::Join"];
        return string.Concat(((object[])join[1]).Select(p => p as string ?? Json(p)));
    }

    private static string StateMachineLogicalId() => Synth().FindResources("AWS::StepFunctions::StateMachine").Keys.Single();

    private static string StateMachinePolicy() =>
        Json(Synth().FindResources("AWS::IAM::Policy").Values.Select(PropsOf)
            .Where(p => Json(p["Roles"]).Contains(StateMachineLogicalId()[..^8]))
            .ToList());

    [Fact]
    public void ComparePairs_RunsOutsideTheVpc_WithBothBedrockModels()
    {
        var fn = Fn(PairsFn).Props;
        Assert.Equal("dotnet8", fn["Runtime"]);
        Assert.Equal("RoomLedger.Functions::RoomLedger.Functions.ComparePairs.Function::HandleAsync", fn["Handler"]);
        Assert.False(fn.ContainsKey("VpcConfig"));   // VPC không có NAT, Bedrock chỉ gọi được từ ngoài VPC
        Assert.False(fn.ContainsKey("ReservedConcurrentExecutions"));
        var env = (IDictionary<string, object>)((IDictionary<string, object>)fn["Environment"])["Variables"];
        Assert.Equal(Sonnet, env["BEDROCK_PRIMARY_MODEL"]);
        Assert.Equal(Haiku, env["BEDROCK_FALLBACK_MODEL"]);
        Assert.Equal("roomledger-dev-derived", env["DERIVED_BUCKET"]);
        Assert.False(env.ContainsKey("ROOMLEDGER_DB"));
    }

    [Fact]
    public void ComparePairs_InvokesOnlyTheTwoClaudeModels_AndOnlyReadsDerived()
    {
        var policy = PolicyOf(Fn(PairsFn).LogicalId);
        Assert.Contains("bedrock:InvokeModel", policy);
        Assert.Contains("inference-profile/" + Sonnet, policy);
        Assert.Contains("inference-profile/" + Haiku, policy);
        Assert.Contains("foundation-model/anthropic.claude-sonnet-4-5-20250929-v1:0", policy);
        Assert.Contains("foundation-model/anthropic.claude-haiku-4-5-20251001-v1:0", policy);
        Assert.Contains("roomledger-dev-derived/*", policy);
        Assert.DoesNotContain("s3:PutObject", policy);
        Assert.DoesNotContain("roomledger-dev-incoming", policy);
        Assert.DoesNotContain("rds-db:connect", policy);
        Assert.DoesNotContain("foundation-model/*", policy);
    }

    [Fact]
    public void PlanReadsDerivedThumbnails_PersistTouchesNoS3_NeitherCallsBedrock()
    {
        var plan = PolicyOf(Fn(PlanFn).LogicalId);
        Assert.Contains("s3:GetObject", plan);
        Assert.Contains("roomledger-dev-derived/*", plan);
        Assert.DoesNotContain("s3:PutObject", plan);
        Assert.DoesNotContain("bedrock", plan);
        var persist = PolicyOf(Fn(PersistFn).LogicalId);
        Assert.DoesNotContain("s3:", persist);
        Assert.DoesNotContain("bedrock", persist);
        Assert.Contains("rds-db:connect", persist);
    }

    [Theory]
    [InlineData("/roomledger/dev/compare/phash-threshold", "6")]
    [InlineData("/roomledger/dev/compare/max-calls", "30")]
    public void CompareSettings_AreSsmParameters_TunableWithoutDeploy(string name, string value)
        => Synth().HasResourceProperties("AWS::SSM::Parameter", new Dictionary<string, object>
        {
            ["Name"] = name,
            ["Value"] = value,
            ["Type"] = "String",
        });

    [Fact]
    public void MoveOutSubmittedFromTheApi_StartsTheStateMachine_WithTheEventDetail()
        => Synth().HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
        {
            ["EventPattern"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["source"] = new[] { "roomledger.api" },
                ["detail-type"] = new[] { "MoveOutSubmitted" },
            }),
            ["Targets"] = Match.ArrayWith([Match.ObjectLike(new Dictionary<string, object>
            {
                ["Arn"] = new Dictionary<string, object> { ["Ref"] = StateMachineLogicalId() },
                ["InputPath"] = "$.detail",
            })]),
        });

    [Fact]
    public void StateMachine_ReadsSettings_Plans_MapsComparePairs_Persists_ThenReports()
    {
        var def = Definition();
        Assert.Contains("aws-sdk:ssm:getParameter", def);
        Assert.Contains("/roomledger/dev/compare/phash-threshold", def);
        Assert.Contains("/roomledger/dev/compare/max-calls", def);
        Assert.Contains(Fn(PlanFn).LogicalId, def);
        Assert.Contains("\"Type\":\"Map\"", def);
        Assert.Contains("\"MaxConcurrency\":3", def);         // quota Bedrock tài khoản mới thấp
        Assert.Contains(Fn(PairsFn).LogicalId, def);
        Assert.Contains("\"Status\":\"NOT_ANALYZED\"", def);  // Catch: lỗi sau khi thử lại → không phân tích được
        Assert.Contains("States.ALL", def);
        Assert.Contains(Fn(PersistFn).LogicalId, def);
        Assert.Contains("aws-sdk:cloudwatch:putMetricData", def);
        Assert.Contains("RoomLedger/Compare", def);
        Assert.Contains("\"DetailType\":\"MoveOutCompared\"", def);   // notify sẽ gửi email chủ nhà
        Assert.Contains("$.persist.Persisted", def);
        Assert.Contains("$.plan.InspectionId", def);
        Assert.Contains("$.MoveOutPhotoId", def);
    }

    [Fact]
    public void StateMachine_HasAStableName()
        => Synth().HasResourceProperties("AWS::StepFunctions::StateMachine", new Dictionary<string, object>
        {
            ["StateMachineName"] = "roomledger-dev-move-out-compare",
        });

    [Fact]
    public void StateMachineRole_InvokesTheFunctions_ReadsSettings_PutsMetricsAndEvents_NoBedrock()
    {
        var policy = StateMachinePolicy();
        Assert.Contains("lambda:InvokeFunction", policy);
        Assert.Contains("ssm:GetParameter", policy);
        Assert.Contains("cloudwatch:PutMetricData", policy);
        Assert.Contains("events:PutEvents", policy);
        Assert.DoesNotContain("bedrock", policy);
    }

    [Fact]
    public void EveryRuleTarget_HasADeadLetterQueue()
    {
        var rules = Synth().FindResources("AWS::Events::Rule").Values.Select(PropsOf).ToList();
        Assert.Equal(5, rules.Count);   // + TenancySettled (MD-06-FE-05)
        foreach (var target in rules.SelectMany(r => (object[])r["Targets"]).Cast<IDictionary<string, object>>())
        {
            Assert.True(target.ContainsKey("DeadLetterConfig"), Json(target));
        }
    }

    [Fact]
    public void Alarms_OnTheDeadLetterQueue_AndOnFailedCompareRuns()
    {
        Synth().HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
        {
            ["MetricName"] = "ApproximateNumberOfMessagesVisible",
            ["Namespace"] = "AWS/SQS",
            ["Threshold"] = 1,
            ["ComparisonOperator"] = "GreaterThanOrEqualToThreshold",
        });
        Synth().HasResourceProperties("AWS::CloudWatch::Alarm", new Dictionary<string, object>
        {
            ["MetricName"] = "ExecutionsFailed",
            ["Namespace"] = "AWS/States",
            ["Threshold"] = 1,
        });
    }
}
