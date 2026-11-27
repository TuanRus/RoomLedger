using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.Cognito;
using RoomLedger.Cdk.Stacks;
using Xunit;

namespace RoomLedger.Cdk.Tests;

/**
 * AiStack: state machine đọc đồng hồ (Step Functions + Lambda ngoài VPC gọi Bedrock), tham số Parameter Store,
 * và Lambda notify gửi email qua SES. Kiểm quyền tối thiểu, chỗ chạy (trong/ngoài VPC) và việc không có địa chỉ cứng.
 *
 * @author Tuấn
 * @version 1.0
 */
public class AiStackTests
{
    private const string ReadFn = "roomledger-dev-read-document";
    private const string PrepareFn = "roomledger-dev-meter-prepare";
    private const string PersistFn = "roomledger-dev-meter-persist";
    private const string NotifyFn = "roomledger-dev-notify";
    private const string Sender = "sender@example.com";

    private static readonly Lazy<Template> WithSes = new(() => Build(Sender, "https://app.example.com"));
    private static readonly Lazy<Template> WithoutSes = new(() => Build(null, null));

    private static string PromptsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "backend", "src", "RoomLedger.Ai", "Prompts")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Không thấy thư mục prompt"), "src", "backend", "src", "RoomLedger.Ai", "Prompts");
    }

    private static string Prompt(string task) => File.ReadAllText(Path.Combine(PromptsDir(), $"{task}.md")).Trim();

    private static Template Build(string? sender, string? webOrigin)
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
        var auth = new Stack(app, "TestUsers", new StackProps { Env = env });
        var users = new UserPool(auth, "Users");
        var ai = new AiStack(app, "TestAi", new AiStackProps
        {
            Stage = "dev", Env = env, Vpc = network.Vpc, LambdaSg = network.LambdaSg, Db = data.Db, FunctionsAssetPath = asset,
            IncomingBucketName = "roomledger-dev-incoming", OutboxBucketName = "roomledger-dev-outbox", Users = users,
            ElectricPrompt = Prompt("readElectricMeter"), WaterPrompt = Prompt("readWaterMeter"),
            SesSender = sender, WebOrigin = webOrigin, AlarmTopic = observability.AlarmTopic,
        });
        return Template.FromStack(ai);
    }

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static IDictionary<string, object> PropsOf(IDictionary<string, object> r) => (IDictionary<string, object>)r["Properties"];

    private static (string LogicalId, IDictionary<string, object> Props) Fn(Template t, string name) =>
        t.FindResources("AWS::Lambda::Function").Select(kv => (kv.Key, PropsOf(kv.Value)))
            .Single(f => f.Item2.TryGetValue("FunctionName", out var n) && n as string == name);

    private static string PolicyOf(Template t, string functionLogicalId) =>
        Json(t.FindResources("AWS::IAM::Policy").Values.Select(PropsOf)
            .Where(p => Json(p["Roles"]).Contains(functionLogicalId[..^8])).ToList());

    private static Template Synth() => WithSes.Value;

    [Fact]
    public void Prompts_FitInAStandardParameter_AndHaveNoPlaceholder()
    {
        foreach (var task in new[] { "readElectricMeter", "readWaterMeter" })
        {
            var text = Prompt(task);
            Assert.True(text.Length < 4096, $"{task}: tham số Standard chứa tối đa 4096 ký tự");
            Assert.DoesNotContain("TODO", text);
        }
    }

    [Fact]
    public void ModelIdAndPrompts_LiveInStandardParameterStore()
    {
        var parameters = Synth().FindResources("AWS::SSM::Parameter").Values.Select(PropsOf).ToList();
        Assert.Equal(3, parameters.Count);
        Assert.All(parameters, p => Assert.Equal("Standard", p["Tier"]));
        Assert.Equal(
            ["/roomledger/dev/ai/model-id", "/roomledger/dev/ai/prompt/readElectricMeter", "/roomledger/dev/ai/prompt/readWaterMeter"],
            parameters.Select(p => (string)p["Name"]).OrderBy(n => n).ToArray());
        Assert.Equal(AiStack.DefaultModelId, parameters.Single(p => ((string)p["Name"]).EndsWith("model-id"))["Value"]);
        Assert.StartsWith("au.", AiStack.DefaultModelId);
    }

    [Fact]
    public void ReadDocument_RunsOutsideTheVpc_WithoutDatabaseAccess()
    {
        var fn = Fn(Synth(), ReadFn).Props;
        Assert.Equal("dotnet8", fn["Runtime"]);
        Assert.Equal("RoomLedger.Functions::RoomLedger.Functions.ReadDocument.Function::HandleAsync", fn["Handler"]);
        Assert.False(fn.ContainsKey("VpcConfig"));
        var env = Json(fn["Environment"]);
        Assert.DoesNotContain("ROOMLEDGER_DB", env);
        Assert.Contains("/roomledger/dev", env);
        Assert.DoesNotContain("rds-db:connect", PolicyOf(Synth(), Fn(Synth(), ReadFn).LogicalId));
    }

    [Fact]
    public void ReadDocument_CanOnlyReadIncoming_ReadItsParameters_AndInvokeTheHaikuProfile()
    {
        var policy = PolicyOf(Synth(), Fn(Synth(), ReadFn).LogicalId);
        Assert.Contains("roomledger-dev-incoming/*", policy);
        Assert.DoesNotContain("s3:PutObject", policy);
        Assert.Contains("ssm:GetParameter", policy);
        Assert.Contains("bedrock:InvokeModel", policy);
        Assert.Contains("inference-profile/au.anthropic.claude-haiku-4-5-*", policy);
        Assert.Contains("foundation-model/anthropic.claude-haiku-4-5-*", policy);
        Assert.Contains("bedrock:InferenceProfileArn", policy);
        // Không dùng Resource "*" ở bất kỳ statement nào của hàm
        Assert.DoesNotContain("\"Resource\":\"*\"", policy);
    }

    [Theory]
    [InlineData(PrepareFn, "PrepareAsync")]
    [InlineData(PersistFn, "PersistAsync")]
    public void PrepareAndPersist_RunInsideTheVpc_AsTheAppUserWithIamAuth(string name, string method)
    {
        var fn = Fn(Synth(), name).Props;
        Assert.Equal($"RoomLedger.Functions::RoomLedger.Functions.MeterResult.Function::{method}", fn["Handler"]);
        Assert.Contains("LambdaSg", Json(((IDictionary<string, object>)fn["VpcConfig"])["SecurityGroupIds"]));
        var cs = Json(((IDictionary<string, object>)((IDictionary<string, object>)fn["Environment"])["Variables"])["ROOMLEDGER_DB"]);
        Assert.Contains("Username=roomledger_app", cs);
        Assert.DoesNotContain("Password", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rds-db:connect", PolicyOf(Synth(), Fn(Synth(), name).LogicalId));
    }

    [Fact]
    public void Persist_CanOnlyWriteTheNeedsReviewEventToTheOutbox_AndPrepareWritesNothing()
    {
        var persist = PolicyOf(Synth(), Fn(Synth(), PersistFn).LogicalId);
        Assert.Contains("roomledger-dev-outbox/events/roomledger.functions/MeterReadingNeedsReview/*", persist);
        Assert.DoesNotContain("roomledger-dev-incoming", persist);
        Assert.DoesNotContain("s3:", PolicyOf(Synth(), Fn(Synth(), PrepareFn).LogicalId));
    }

    [Fact]
    public void StateMachine_PreparesReadsWithRetriesAndAlwaysEndsInPersist()
    {
        var machine = Synth().FindResources("AWS::StepFunctions::StateMachine").Values.Select(PropsOf).Single();
        Assert.Equal("roomledger-dev-read-meter", machine["StateMachineName"]);
        Assert.Equal("STANDARD", machine["StateMachineType"]);
        var definition = Json(machine["DefinitionString"]).Replace("\\u0022", "\"").Replace("\\\"", "\"");
        Assert.Contains("Is it a meter photo awaiting AI", definition);
        Assert.Contains("\"Retry\"", definition);
        Assert.Contains("\"Catch\"", definition);
        Assert.Contains("Save the failure for manual entry", definition);
        Assert.Contains("Save the reading", definition);
        Assert.Equal(1, Synth().FindResources("AWS::Logs::LogGroup").Values.Select(PropsOf)
            .Count(l => ((string)l["LogGroupName"]).StartsWith("/aws/vendedlogs/states/")));
    }

    [Fact]
    public void ObjectCreatedInIncoming_StartsTheStateMachine_WithTheDlq()
        => Synth().HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
        {
            ["EventPattern"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["detail-type"] = new[] { "Object Created" },
                ["detail"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["bucket"] = new Dictionary<string, object> { ["name"] = new[] { "roomledger-dev-incoming" } },
                }),
            }),
            ["Targets"] = Match.ArrayWith([Match.ObjectLike(new Dictionary<string, object>
            {
                ["DeadLetterConfig"] = Match.AnyValue(),
                ["RetryPolicy"] = Match.ObjectLike(new Dictionary<string, object> { ["MaximumRetryAttempts"] = 2 }),
            })]),
        });

    [Fact]
    public void Alarms_WatchTheDlqAndFailedExecutions_AndNotifyTheAlarmTopic()
    {
        var alarms = Synth().FindResources("AWS::CloudWatch::Alarm").Values.Select(PropsOf).ToList();
        Assert.Equal(["roomledger-dev-ai-dlq-not-empty", "roomledger-dev-read-meter-failed"], alarms.Select(a => (string)a["AlarmName"]).OrderBy(n => n).ToArray());
        Assert.All(alarms, a => Assert.True(a.ContainsKey("AlarmActions")));
    }

    [Fact]
    public void Notify_RunsOutsideTheVpc_SendsOnlyFromTheConfiguredSender_AndFailuresGoToTheDlq()
    {
        var (id, fn) = Fn(Synth(), NotifyFn);
        Assert.False(fn.ContainsKey("VpcConfig"));
        Assert.Equal("RoomLedger.Functions::RoomLedger.Functions.Notify.Function::HandleAsync", fn["Handler"]);
        Assert.Contains("DeadLetterConfig", Json(fn.Keys));
        var env = Json(fn["Environment"]);
        Assert.Contains(Sender, env);
        Assert.Contains("https://app.example.com", env);
        var policy = PolicyOf(Synth(), id);
        Assert.Contains("ses:SendEmail", policy);
        Assert.Contains("ses:FromAddress", policy);
        Assert.Contains("cognito-idp:ListUsers", policy);
        Assert.Contains("roomledger-dev-outbox/events/roomledger.api/InvoiceSent/*", policy);
        Assert.DoesNotContain("PhotoReceived", policy);
        Assert.DoesNotContain("rds-db:connect", policy);
        Synth().HasResourceProperties("AWS::Lambda::EventInvokeConfig", new Dictionary<string, object>
        {
            ["FunctionName"] = new Dictionary<string, object> { ["Ref"] = id },
            ["MaximumRetryAttempts"] = 2,
        });
    }

    [Fact]
    public void Notify_RuleOnlyMatchesEventsThatHaveAnEmailTemplate()
    {
        var rule = Synth().FindResources("AWS::Events::Rule").Values.Select(PropsOf)
            .Single(r => Json(r["EventPattern"]).Contains("roomledger-dev-outbox"));
        var pattern = Json(rule["EventPattern"]);
        foreach (var prefix in AiStack.NotifyKeyPrefixes())
        {
            Assert.Contains($"\"prefix\":\"{prefix}\"", pattern);
        }

        Assert.DoesNotContain("PhotoReceived", pattern);
        Assert.DoesNotContain("InspectionLocked", pattern);
    }

    [Fact]
    public void WithoutASesSender_NoNotifyLambdaOrRuleIsBuilt_AndNoAddressIsHardCoded()
    {
        var t = WithoutSes.Value;
        Assert.DoesNotContain(t.FindResources("AWS::Lambda::Function").Values.Select(PropsOf), f => f.TryGetValue("FunctionName", out var n) && n as string == NotifyFn);
        Assert.DoesNotContain("ses:", Json(t.FindResources("AWS::IAM::Policy")));
        Assert.DoesNotContain("@", Json(t.ToJSON()));
    }

    [Fact]
    public void SesSenderWithoutWebOrigin_IsRejected()
        => Assert.Throws<InvalidOperationException>(() => Build(Sender, null));

    [Fact]
    public void EveryDotnetLambda_RunsOnArm64()
    {
        // Gói Lambda đóng bằng --function-architecture arm64 (Magick.NET, QuestPDF có native linux-arm64)
        var lambdas = WithSes.Value.FindResources("AWS::Lambda::Function").Values
            .Select(r => (IDictionary<string, object>)((IDictionary<string, object>)r)["Properties"])
            .Where(p => p.TryGetValue("Runtime", out var rt) && "dotnet8".Equals(rt)).ToList();
        Assert.NotEmpty(lambdas);
        Assert.All(lambdas, p => Assert.Equal(new[] { "arm64" }, ((IEnumerable<object>)p["Architectures"]).Cast<string>().ToArray()));
    }
}
