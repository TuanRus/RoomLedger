using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using RoomLedger.Cdk.Stacks;
using Xunit;

namespace RoomLedger.Cdk.Tests;

/**
 * StorageStack (4 bucket, Object Lock), AuthStack (2 User Pool, PostConfirmation) và ApiStack (REST API,
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
            OutboxBucketName = "roomledger-dev-outbox",
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

    private const string DemoOrigin = "https://demo.roomledger.test";

    private static StorageStack NewDemoStorage(string? webOrigin) =>
        new(new App(), "TestDemoStorage", new StorageStackProps
        {
            Stage = "demo",
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "ap-southeast-2" },
            IncomingBucketName = "roomledger-demo-incoming",
            DerivedBucketName = "roomledger-demo-derived",
            EvidenceBucketName = "roomledger-demo-evidence",
            OutboxBucketName = "roomledger-demo-outbox",
            WebOrigin = webOrigin,
        });

    private static readonly Lazy<Template> DemoStorage = new(() => Template.FromStack(NewDemoStorage(DemoOrigin)));

    private static string IncomingCorsJson(Template t) => Json(t.FindResources("AWS::S3::Bucket", new Dictionary<string, object>
    {
        ["Properties"] = Match.ObjectLike(new Dictionary<string, object> { ["CorsConfiguration"] = Match.AnyValue() }),
    }).Values.Single());

    [Fact]
    public void Storage_Dev_WithoutWebOrigin_AllowsAnyOrigin()
    {
        Assert.Contains("\"AllowedOrigins\":[\"*\"]", IncomingCorsJson(Templates.Value.Storage));
    }

    [Fact]
    public void Storage_Demo_UsesTheGivenWebOrigin()
    {
        var cors = IncomingCorsJson(DemoStorage.Value);
        Assert.Contains($"\"AllowedOrigins\":[\"{DemoOrigin}\"]", cors);
        Assert.DoesNotContain("\"*\"", cors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*")]
    public void Storage_Demo_WithoutConcreteWebOrigin_FailsAtSynth(string? webOrigin)
    {
        var ex = Assert.Throws<ArgumentException>(() => NewDemoStorage(webOrigin));
        Assert.Contains("webOrigin", ex.Message);
    }

    private static string EvidenceJson(Template t) => Json(t.FindResources("AWS::S3::Bucket", new Dictionary<string, object>
    {
        ["Properties"] = Match.ObjectLike(new Dictionary<string, object> { ["ObjectLockEnabled"] = true }),
    }).Values.Single());

    [Fact]
    public void Storage_Dev_EvidenceRetainsOneDay_AndNeverGoesToDeepArchive()
    {
        var evidence = EvidenceJson(Templates.Value.Storage);
        Assert.Contains("\"Mode\":\"GOVERNANCE\"", evidence);
        Assert.Matches("\"Days\":1[,}]", evidence);
        Assert.DoesNotContain("DEEP_ARCHIVE", evidence);
    }

    [Fact]
    public void Storage_Demo_EvidenceRetains365Days_AndArchivesClosedTenancies()
    {
        var evidence = EvidenceJson(DemoStorage.Value);
        Assert.Matches("\"Days\":365[,}]", evidence);
        Assert.Contains("DEEP_ARCHIVE", evidence);
        Assert.Contains("tenancy-status", evidence);
    }

    [Fact]
    public void Storage_EvidenceUsesTheCmk_WithRotation_AndTheKeyIsRetained()
    {
        var storage = Templates.Value.Storage;
        storage.ResourceCountIs("AWS::KMS::Key", 1);   // một khóa duy nhất cho cả hệ thống
        storage.HasResourceProperties("AWS::KMS::Key", new Dictionary<string, object> { ["EnableKeyRotation"] = true });
        storage.HasResource("AWS::KMS::Key", new Dictionary<string, object> { ["DeletionPolicy"] = "Retain" });
        storage.HasResourceProperties("AWS::KMS::Alias", new Dictionary<string, object> { ["AliasName"] = "alias/roomledger-dev-evidence" });

        var evidence = EvidenceJson(storage);
        Assert.Contains("aws:kms", evidence);
        Assert.Contains("EvidenceKey", evidence);
        Assert.Contains("\"BucketKeyEnabled\":true", evidence);
        // 3 bucket còn lại vẫn dùng SSE-S3
        Assert.Equal(3, storage.FindResources("AWS::S3::Bucket").Values.Count(b => Json(b).Contains("AES256")));
    }

    [Fact]
    public void Storage_KeyPolicy_LetsAccountPrincipalsUseTheKeyOnlyThroughS3()
    {
        var key = Json(Templates.Value.Storage.FindResources("AWS::KMS::Key").Values.Single());
        Assert.Contains("kms:ViaService", key);
        Assert.Contains("s3.ap-southeast-2.amazonaws.com", key);
        Assert.Contains("kms:CallerAccount", key);
    }

    [Fact]
    public void Storage_HasFourBuckets_WithTheNamesWorkflowStackUses()
    {
        var storage = Templates.Value.Storage;
        storage.ResourceCountIs("AWS::S3::Bucket", 4);
        foreach (var name in new[] { "roomledger-dev-incoming", "roomledger-dev-derived", "roomledger-dev-evidence", "roomledger-dev-outbox" })
        {
            storage.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object> { ["BucketName"] = name });
        }
    }

    // CDK bật EventBridge qua custom resource Custom::S3BucketNotifications, không ghi vào AWS::S3::Bucket
    [Theory]
    [InlineData("roomledger-dev-incoming")]
    [InlineData("roomledger-dev-outbox")]
    public void Storage_SendsEventsToEventBridge(string bucket)
    {
        var storage = Templates.Value.Storage;
        var bucketId = storage.FindResources("AWS::S3::Bucket", new Dictionary<string, object>
        {
            ["Properties"] = Match.ObjectLike(new Dictionary<string, object> { ["BucketName"] = bucket }),
        }).Keys.Single();
        var notifications = storage.FindResources("Custom::S3BucketNotifications").Values.Select(Json).ToList();
        Assert.Equal(2, notifications.Count);
        var single = Assert.Single(notifications, n => n.Contains(bucketId));
        Assert.Contains("EventBridgeConfiguration", single);
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

    /** D1 (BR-17): API xóa được ảnh ở incoming và bản xem *.jpg ở derived; không xóa được evidence hay PDF thanh lý. */
    [Fact]
    public void Api_CanDeleteOnlyIncomingAndDerivedJpg_NeverEvidenceNorReports()
    {
        var statements = Templates.Value.Api.FindResources("AWS::IAM::Policy").Values
            .Select(r => (IDictionary<string, object>)r["Properties"])
            .Select(p => (IDictionary<string, object>)p["PolicyDocument"])
            .SelectMany(d => ((object[])d["Statement"]).Cast<IDictionary<string, object>>())
            .ToList();

        var json = Json(Assert.Single(statements, s => Json(s).Contains("s3:DeleteObject")));

        Assert.Contains("Incoming", json);
        Assert.Contains("Derived", json);
        Assert.Contains("/*.jpg", json);
        Assert.Contains("/qr/*", json);   // mã QR nhà trọ
        Assert.DoesNotContain("Evidence", json);
        Assert.DoesNotContain("reports", json);
    }

    /** Mã QR nhà trọ: API chỉ được s3:PutObject (không Get/Delete kèm theo) vào derived/qr/*, không ghi được chỗ khác của derived. */
    [Fact]
    public void Api_CanPutOnlyUnderDerivedQrPrefix()
    {
        var statements = Templates.Value.Api.FindResources("AWS::IAM::Policy").Values
            .Select(r => (IDictionary<string, object>)r["Properties"])
            .Select(p => (IDictionary<string, object>)p["PolicyDocument"])
            .SelectMany(d => ((object[])d["Statement"]).Cast<IDictionary<string, object>>())
            .ToList();

        var puts = statements.Where(s => Json(s).Contains("s3:PutObject")).ToList();
        var qr = Json(Assert.Single(puts, s => Json(s).Contains("PutPropertyQr")));
        Assert.Contains("Derived", qr);
        Assert.Contains("/qr/*", qr);
        Assert.DoesNotContain("Evidence", qr);
        Assert.DoesNotContain("s3:GetObject", qr);
        Assert.DoesNotContain("s3:DeleteObject", qr);
        // Không còn statement nào cho phép ghi vào derived ngoài qr/*
        Assert.DoesNotContain(puts.Where(s => !Json(s).Contains("PutPropertyQr")), s => Json(s).Contains("Derived"));
    }

    /** Sự kiện: API chỉ ghi được outbox dưới tiền tố của mình, không còn quyền events:PutEvents. */
    [Fact]
    public void Api_PublishesOnlyThroughItsOutboxPrefix()
    {
        var policies = Json(Templates.Value.Api.FindResources("AWS::IAM::Policy"));
        Assert.Contains("PublishToOutbox", policies);
        Assert.Contains("/events/roomledger.api/*", policies);
        Assert.DoesNotContain("events:PutEvents", policies);
    }
}
