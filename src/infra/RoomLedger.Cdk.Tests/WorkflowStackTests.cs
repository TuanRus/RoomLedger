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
 * @version 1.0
 */
public class WorkflowStackTests
{
    private const string Ingest = "roomledger-dev-photo-ingest";
    private const string Derive = "roomledger-dev-photo-derive";
    private const string SealFn = "roomledger-dev-evidence-seal";

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
}
