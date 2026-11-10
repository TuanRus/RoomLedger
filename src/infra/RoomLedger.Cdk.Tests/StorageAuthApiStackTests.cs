using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using RoomLedger.Cdk.Stacks;
using Xunit;

namespace RoomLedger.Cdk.Tests;

/**
 * StorageStack (3 bucket, Object Lock), AuthStack (2 User Pool, PostConfirmation) và ApiStack (REST API,
 * Cognito authorizer, Lambda api trong VPC) nối với Network + Data của develop.
 *
 * @author Tuấn
 * @version 1.0
 */
public class StorageAuthApiStackTests
{
    private static readonly Lazy<(Template Storage, Template Auth, Template Api)> Templates = new(Build);

    private static (Template, Template, Template) Build()
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
        var storage = new StorageStack(app, "TestStorage", new StorageStackProps
        {
            Stage = "dev",
            Env = env,
            IncomingBucketName = "roomledger-dev-incoming",
            DerivedBucketName = "roomledger-dev-derived",
            EvidenceBucketName = "roomledger-dev-evidence",
        });
        var auth = new AuthStack(app, "TestAuth", new AuthStackProps
        {
            Stage = "dev", Env = env, Vpc = network.Vpc, LambdaSg = network.LambdaSg, Db = data.Db, FunctionsAssetPath = asset,
        });
        var api = new ApiStack(app, "TestApi", new ApiStackProps
        {
            Stage = "dev",
            Env = env,
            Vpc = network.Vpc,
            LambdaSg = network.LambdaSg,
            Db = data.Db,
            Storage = storage,
            Auth = auth,
            ApiAssetPath = asset,
        });
        return (Template.FromStack(storage), Template.FromStack(auth), Template.FromStack(api));
    }

    private static string Json(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public void Storage_HasThreeBuckets_WithTheNamesWorkflowStackUses()
    {
        var storage = Templates.Value.Storage;
        storage.ResourceCountIs("AWS::S3::Bucket", 3);
        foreach (var name in new[] { "roomledger-dev-incoming", "roomledger-dev-derived", "roomledger-dev-evidence" })
        {
            storage.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object> { ["BucketName"] = name });
        }
    }

    // CDK bật EventBridge qua custom resource Custom::S3BucketNotifications, không ghi vào AWS::S3::Bucket
    [Fact]
    public void Storage_IncomingSendsEventsToEventBridge()
    {
        var storage = Templates.Value.Storage;
        var incomingId = storage.FindResources("AWS::S3::Bucket", new Dictionary<string, object>
        {
            ["Properties"] = Match.ObjectLike(new Dictionary<string, object> { ["BucketName"] = "roomledger-dev-incoming" }),
        }).Keys.Single();
        var notifications = storage.FindResources("Custom::S3BucketNotifications");
        var single = Assert.Single(notifications.Values);
        var json = Json(single);
        Assert.Contains("EventBridgeConfiguration", json);
        Assert.Contains(incomingId, json);
    }

    [Fact]
    public void Storage_EvidenceHasVersioningAndObjectLock_AndIsRetained()
    {
        var storage = Templates.Value.Storage;
        storage.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
        {
            ["BucketName"] = "roomledger-dev-evidence",
            ["ObjectLockEnabled"] = true,
            ["VersioningConfiguration"] = new Dictionary<string, object> { ["Status"] = "Enabled" },
        });
        storage.HasResource("AWS::S3::Bucket", new Dictionary<string, object>
        {
            ["Properties"] = Match.ObjectLike(new Dictionary<string, object> { ["BucketName"] = "roomledger-dev-evidence" }),
            ["DeletionPolicy"] = "Retain",
        });
    }

    [Fact]
    public void Storage_AllBucketsBlockPublicAccess()
    {
        var buckets = Templates.Value.Storage.FindResources("AWS::S3::Bucket");
        Assert.All(buckets.Values, b => Assert.Contains("\"BlockPublicAcls\":true", Json(b)));
    }

    [Fact]
    public void Auth_HasUsersPoolWithPostConfirmation_AndAdminsPoolWithMfa()
    {
        var auth = Templates.Value.Auth;
        auth.ResourceCountIs("AWS::Cognito::UserPool", 2);
        auth.HasResourceProperties("AWS::Cognito::UserPool", new Dictionary<string, object>
        {
            ["UserPoolName"] = "roomledger-dev-users",
            ["LambdaConfig"] = Match.ObjectLike(new Dictionary<string, object> { ["PostConfirmation"] = Match.AnyValue() }),
        });
        auth.HasResourceProperties("AWS::Cognito::UserPool", new Dictionary<string, object>
        {
            ["UserPoolName"] = "roomledger-dev-admins",
            ["MfaConfiguration"] = "ON",
        });
    }

    [Fact]
    public void Auth_PostConfirmationRunsInVpc_AsAppUserWithoutPassword()
    {
        var fn = Json(Templates.Value.Auth.FindResources("AWS::Lambda::Function"));
        Assert.Contains("roomledger-dev-post-confirmation", fn);
        Assert.Contains("VpcConfig", fn);
        Assert.Contains("Username=roomledger_app", fn);
        Assert.DoesNotContain("Password", fn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rds-db:connect", Json(Templates.Value.Auth.FindResources("AWS::IAM::Policy")));
    }

    [Fact]
    public void Api_UsesCognitoAuthorizer_AndRegionalRestApi()
    {
        var api = Templates.Value.Api;
        api.HasResourceProperties("AWS::ApiGateway::Authorizer", new Dictionary<string, object> { ["Type"] = "COGNITO_USER_POOLS" });
        api.HasResourceProperties("AWS::ApiGateway::RestApi", new Dictionary<string, object>
        {
            ["Name"] = "roomledger-dev-api",
            ["EndpointConfiguration"] = new Dictionary<string, object> { ["Types"] = new[] { "REGIONAL" } },
        });
    }

    [Fact]
    public void Api_LambdaRunsDotnet8InVpc_WithIamDbAuth()
    {
        var fn = Json(Templates.Value.Api.FindResources("AWS::Lambda::Function"));
        Assert.Contains("\"Runtime\":\"dotnet8\"", fn);
        Assert.Contains("VpcConfig", fn);
        Assert.Contains("Username=roomledger_app", fn);
        Assert.DoesNotContain("ReservedConcurrentExecutions", fn); // giới hạn tài khoản là 10
        Assert.Contains("rds-db:connect", Json(Templates.Value.Api.FindResources("AWS::IAM::Policy")));
    }
}
