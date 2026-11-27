using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của EdgeStack.
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed class EdgeStackProps : StackProps
{
    public required string Stage { get; init; }
    /** API Gateway của ApiStack: CloudFront chuyển /api/* tới stage này. */
    public required IRestApi Api { get; init; }
}

/**
 * Cửa vào duy nhất của web: bucket S3 chứa web (private, chỉ CloudFront đọc qua OAC) và CloudFront.
 * Đường dẫn /api/* chuyển sang API Gateway (không cache, chuyển nguyên Authorization và query), còn lại phục vụ web từ S3.
 * Web và API cùng một domain nên trình duyệt không cần CORS cho API.
 * Không gắn WAF CloudFront: WAF phạm vi CloudFront bắt buộc nằm ở us-east-1 mà SCP đang chặn, nên stack này chạy cùng region với API.
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed class EdgeStack : Stack
{
    public Bucket WebBucket { get; }
    public Distribution Distribution { get; }

    // Route phía client của SPA (không có dấu chấm) trả index.html; file tĩnh (.js, .css, ảnh) giữ nguyên.
    // Dùng function ở behavior mặc định thay vì customErrorResponses để lỗi 403/404 của /api không bị đổi thành index.html.
    private const string SpaRewrite = @"function handler(event) {
  var request = event.request;
  if (request.uri.indexOf('.') === -1) {
    request.uri = '/index.html';
  }
  return request;
}";

    public EdgeStack(Construct scope, string id, EdgeStackProps props) : base(scope, id, props)
    {
        var isDev = props.Stage == "dev";

        WebBucket = new Bucket(this, "Web", new BucketProps
        {
            BucketName = $"{Naming.Resource(props.Stage, "web")}-{Aws.ACCOUNT_ID}",
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            RemovalPolicy = isDev ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN,
        });

        var spaRewrite = new Function(this, "SpaRewrite", new FunctionProps
        {
            FunctionName = Naming.Resource(props.Stage, "spa-rewrite"),
            Runtime = FunctionRuntime.JS_2_0,
            Code = FunctionCode.FromInline(SpaRewrite),
        });

        var apiOrigin = new HttpOrigin(
            $"{props.Api.RestApiId}.execute-api.{Aws.REGION}.{Aws.URL_SUFFIX}",
            new HttpOriginProps { OriginPath = $"/{props.Stage}", ProtocolPolicy = OriginProtocolPolicy.HTTPS_ONLY });

        Distribution = new Distribution(this, "Cdn", new DistributionProps
        {
            Comment = $"{Naming.Project} {props.Stage} web + api",
            DefaultRootObject = "index.html",
            PriceClass = PriceClass.PRICE_CLASS_100,
            HttpVersion = HttpVersion.HTTP2_AND_3,
            DefaultBehavior = new BehaviorOptions
            {
                Origin = S3BucketOrigin.WithOriginAccessControl(WebBucket),
                ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
                AllowedMethods = AllowedMethods.ALLOW_GET_HEAD,
                CachePolicy = CachePolicy.CACHING_OPTIMIZED,
                ResponseHeadersPolicy = ResponseHeadersPolicy.SECURITY_HEADERS,
                Compress = true,
                FunctionAssociations =
                [
                    new FunctionAssociation { EventType = FunctionEventType.VIEWER_REQUEST, Function = spaRewrite },
                ],
            },
            AdditionalBehaviors = new Dictionary<string, IBehaviorOptions>
            {
                ["/api/*"] = new BehaviorOptions
                {
                    Origin = apiOrigin,
                    ViewerProtocolPolicy = ViewerProtocolPolicy.HTTPS_ONLY,
                    AllowedMethods = AllowedMethods.ALLOW_ALL,
                    CachePolicy = CachePolicy.CACHING_DISABLED,
                    OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
                },
            },
        });

        _ = new CfnOutput(this, "WebBucketName", new CfnOutputProps { Value = WebBucket.BucketName });
        _ = new CfnOutput(this, "WebUrl", new CfnOutputProps { Value = $"https://{Distribution.DistributionDomainName}" });
    }
}
