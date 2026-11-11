using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của StorageStack. Tên bucket do Program.cs truyền vào, dùng chung với WorkflowStack.
 *
 * @author Tuấn
 * @version 1.3
 */
public sealed class StorageStackProps : StackProps
{
    public required string Stage { get; init; }
    public required string IncomingBucketName { get; init; }
    public required string DerivedBucketName { get; init; }
    public required string EvidenceBucketName { get; init; }
    /**
     * Origin của web cho CORS PUT vào bucket incoming, ví dụ https://demo.example.com.
     * Bắt buộc ở stage khác dev (không được bỏ trống hay "*"); dev bỏ trống thì dùng "*".
     */
    public string? WebOrigin { get; init; }
}

/**
 * 3 bucket incoming / evidence (Object Lock) / derived, lifecycle, chặn truy cập công khai, bắt buộc TLS.
 * Bucket incoming bật EventBridge để WorkflowStack (photo-ingest) nhận sự kiện "Object Created".
 * Phát rà soát và hoàn thiện bucket policy (StorageStack cần Phát duyệt).
 *
 * @author Tuấn
 * @version 1.3
 */
public sealed class StorageStack : Stack
{
    public Bucket Incoming { get; }
    public Bucket Evidence { get; }
    public Bucket Derived { get; }

    public StorageStack(Construct scope, string id, StorageStackProps props) : base(scope, id, props)
    {
        var isDev = props.Stage == "dev";
        var webOrigin = ResolveWebOrigin(props.Stage, props.WebOrigin);

        Incoming = NewBucket("Incoming", props.IncomingBucketName, isDev ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN, new BucketProps
        {
            EventBridgeEnabled = true,
            LifecycleRules = [new LifecycleRule { Id = "expire-7-days", Expiration = Duration.Days(7) }],
            Cors =
            [
                new CorsRule
                {
                    AllowedMethods = [HttpMethods.PUT],
                    AllowedOrigins = [webOrigin],
                    AllowedHeaders = ["Content-Type", "x-amz-checksum-sha256", "x-amz-sdk-checksum-algorithm"],
                    MaxAge = 300,
                },
            ],
        });

        // dev: giữ 1 ngày để dọn được nhanh, không chuyển Deep Archive (tính tối thiểu 180 ngày, phí cho ảnh test).
        // demo: 365 ngày, chuyển Deep Archive 30 ngày sau khi hợp đồng đóng. Đổi retention sau không áp lại cho object đã ghi.
        Evidence = NewBucket("Evidence", props.EvidenceBucketName, RemovalPolicy.RETAIN, new BucketProps
        {
            Versioned = true,
            ObjectLockEnabled = true,
            // Governance ở dev/demo để còn dọn được khi cần; production dùng Compliance
            ObjectLockDefaultRetention = ObjectLockRetention.Governance(Duration.Days(isDev ? 1 : 365)),
            LifecycleRules = isDev
                ? null
                : new ILifecycleRule[]
                {
                    new LifecycleRule
                    {
                        Id = "closed-tenancy-deep-archive",
                        TagFilters = new Dictionary<string, object> { ["tenancy-status"] = "closed" },
                        Transitions = [new Transition { StorageClass = StorageClass.DEEP_ARCHIVE, TransitionAfter = Duration.Days(30) }],
                    },
                },
        });

        Derived = NewBucket("Derived", props.DerivedBucketName, isDev ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN, new BucketProps());

        new CfnOutput(this, "IncomingBucket", new CfnOutputProps { Value = Incoming.BucketName });
        new CfnOutput(this, "EvidenceBucket", new CfnOutputProps { Value = Evidence.BucketName });
        new CfnOutput(this, "DerivedBucket", new CfnOutputProps { Value = Derived.BucketName });
    }

    /**
     * dev: bỏ trống thì dùng "*". Stage khác phải có origin cụ thể, thiếu thì lỗi ngay lúc synth
     * để bucket incoming của demo không nhận PUT từ mọi trang web.
     */
    private static string ResolveWebOrigin(string stage, string? webOrigin)
    {
        var origin = webOrigin?.Trim();
        if (stage == "dev")
            return string.IsNullOrEmpty(origin) ? "*" : origin;
        if (string.IsNullOrEmpty(origin) || origin == "*")
            throw new ArgumentException(
                $"Stage '{stage}' cần origin cụ thể cho CORS của bucket incoming: -c stage={stage} -c webOrigin=https://<domain web>",
                nameof(webOrigin));
        return origin;
    }

    private Bucket NewBucket(string id, string name, RemovalPolicy removal, BucketProps extra) =>
        new(this, id, new BucketProps
        {
            BucketName = name,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            ObjectOwnership = ObjectOwnership.BUCKET_OWNER_ENFORCED,
            RemovalPolicy = removal,
            AutoDeleteObjects = false,
            EventBridgeEnabled = extra.EventBridgeEnabled,
            LifecycleRules = extra.LifecycleRules,
            Cors = extra.Cors,
            Versioned = extra.Versioned,
            ObjectLockEnabled = extra.ObjectLockEnabled,
            ObjectLockDefaultRetention = extra.ObjectLockDefaultRetention,
        });
}
