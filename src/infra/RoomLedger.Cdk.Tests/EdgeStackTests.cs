using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Amazon.CDK.AWS.APIGateway;
using RoomLedger.Cdk.Stacks;
using Xunit;

namespace RoomLedger.Cdk.Tests;

/**
 * EdgeStack: bucket web private chỉ CloudFront đọc (OAC), /api/* chuyển sang API Gateway không cache,
 * route SPA trả index.html mà không che lỗi của API.
 *
 * @author Tuấn
 * @version 1.0
 */
public class EdgeStackTests
{
    private static readonly Lazy<Template> Dev = new(() => Build("dev"));
    private static readonly Lazy<Template> Prod = new(() => Build("prod"));

    private static Template Build(string stage)
    {
        var app = new App();
        var env = new Amazon.CDK.Environment { Account = "123456789012", Region = "ap-southeast-2" };
        var apiStack = new Stack(app, $"TestApi-{stage}", new StackProps { Env = env });
        var api = new RestApi(apiStack, "Rest", new RestApiProps());
        api.Root.AddMethod("GET");
        var edge = new EdgeStack(app, $"TestEdge-{stage}", new EdgeStackProps { Env = env, Stage = stage, Api = api });
        return Template.FromStack(edge);
    }

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static IDictionary<string, object> DistributionConfig(Template t)
    {
        var props = (IDictionary<string, object>)((IDictionary<string, object>)t.FindResources("AWS::CloudFront::Distribution").Values.Single())["Properties"];
        return (IDictionary<string, object>)props["DistributionConfig"];
    }

    [Fact]
    public void WebBucket_IsPrivate_Encrypted_AndTlsOnly()
    {
        Dev.Value.ResourceCountIs("AWS::S3::Bucket", 1);
        Dev.Value.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
        {
            ["PublicAccessBlockConfiguration"] = new Dictionary<string, object>
            {
                ["BlockPublicAcls"] = true,
                ["BlockPublicPolicy"] = true,
                ["IgnorePublicAcls"] = true,
                ["RestrictPublicBuckets"] = true,
            },
        });
        var policy = Json(Dev.Value.FindResources("AWS::S3::BucketPolicy").Values.Single());
        Assert.Contains("aws:SecureTransport", policy);
        Assert.Contains("cloudfront.amazonaws.com", policy);   // chỉ CloudFront đọc, qua OAC
        Assert.Contains("AWS:SourceArn", policy);
    }

    [Fact]
    public void Distribution_UsesOac_AndRedirectsToHttps()
    {
        Dev.Value.ResourceCountIs("AWS::CloudFront::OriginAccessControl", 1);
        var cfg = DistributionConfig(Dev.Value);
        var behavior = (IDictionary<string, object>)cfg["DefaultCacheBehavior"];
        Assert.Equal("redirect-to-https", behavior["ViewerProtocolPolicy"]);
        Assert.Equal("index.html", cfg["DefaultRootObject"]);
        Assert.Contains("OriginAccessControlId", Json(cfg["Origins"]));
    }

    [Fact]
    public void ApiBehavior_ForwardsToApiGatewayStage_WithoutCaching()
    {
        var cfg = DistributionConfig(Dev.Value);
        var behaviors = ((IEnumerable<object>)cfg["CacheBehaviors"]).Cast<IDictionary<string, object>>().ToList();
        var api = Assert.Single(behaviors);
        Assert.Equal("/api/*", api["PathPattern"]);
        Assert.Equal("https-only", api["ViewerProtocolPolicy"]);
        Assert.Equal(7, ((IEnumerable<object>)api["AllowedMethods"]).Count());   // gồm POST/PUT/PATCH/DELETE
        Assert.Equal("4135ea2d-6df8-44a3-9df3-4b5a84be39ad", api["CachePolicyId"]);   // CachingDisabled
        Assert.Equal("b689b0a8-53d0-40ab-baf2-68738e2966ac", api["OriginRequestPolicyId"]);   // AllViewerExceptHostHeader (giữ Authorization)

        var origins = Json(cfg["Origins"]);
        Assert.Contains("execute-api", origins);
        Assert.Contains("\"OriginPath\":\"/dev\"", origins);
    }

    [Fact]
    public void SpaRewrite_IsOnDefaultBehaviorOnly_AndApiErrorsAreNotMasked()
    {
        Dev.Value.ResourceCountIs("AWS::CloudFront::Function", 1);
        var cfg = DistributionConfig(Dev.Value);
        Assert.Contains("viewer-request", Json(((IDictionary<string, object>)cfg["DefaultCacheBehavior"])["FunctionAssociations"]));
        Assert.False(cfg.ContainsKey("CustomErrorResponses"));
    }

    [Fact]
    public void WebBucket_IsDestroyedInDev_AndRetainedOutsideDev()
    {
        Assert.Contains("\"DeletionPolicy\":\"Delete\"", Json(Dev.Value.FindResources("AWS::S3::Bucket").Values.Single()));
        Assert.Contains("\"DeletionPolicy\":\"Retain\"", Json(Prod.Value.FindResources("AWS::S3::Bucket").Values.Single()));
    }
}
