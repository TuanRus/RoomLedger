using Amazon.CDK;
using Amazon.CDK.AWS.S3;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * 3 bucket incoming / evidence (Object Lock) / derived, lifecycle, chặn truy cập công khai, bắt buộc TLS.
 * Bản nền để API ký presigned URL được; Phát rà soát và hoàn thiện bucket policy (StorageStack cần Phát duyệt).
 *
 * @author Tuấn
 * @version 1.0
 */
public class StorageStack : Stack
{
    public Bucket Incoming { get; }
    public Bucket Evidence { get; }
    public Bucket Derived { get; }

    public StorageStack(Construct scope, string id, StageConfig cfg, IStackProps? props = null) : base(scope, id, props)
    {
        Incoming = NewBucket(cfg, "incoming", new BucketProps
        {
            EventBridgeEnabled = true,
            LifecycleRules = [new LifecycleRule { Id = "expire-7-days", Expiration = Duration.Days(7) }],
            Cors =
            [
                new CorsRule
                {
                    AllowedMethods = [HttpMethods.PUT],
                    AllowedOrigins = [(string?)Node.TryGetContext("webOrigin") ?? "*"],
                    AllowedHeaders = ["Content-Type", "x-amz-checksum-sha256", "x-amz-sdk-checksum-algorithm"],
                    MaxAge = 300,
                },
            ],
        });

        Evidence = NewBucket(cfg, "evidence", new BucketProps
        {
            Versioned = true,
            ObjectLockEnabled = true,
            // Governance ở dev/demo để còn dọn được khi cần; production dùng Compliance
            ObjectLockDefaultRetention = ObjectLockRetention.Governance(Duration.Days(365)),
            LifecycleRules =
            [
                new LifecycleRule
                {
                    Id = "closed-tenancy-deep-archive",
                    TagFilters = new Dictionary<string, object> { ["tenancy-status"] = "closed" },
                    Transitions = [new Transition { StorageClass = StorageClass.DEEP_ARCHIVE, TransitionAfter = Duration.Days(30) }],
                },
            ],
        });

        Derived = NewBucket(cfg, "derived", new BucketProps());
    }

    private Bucket NewBucket(StageConfig cfg, string purpose, BucketProps extra) =>
        new(this, char.ToUpperInvariant(purpose[0]) + purpose[1..], new BucketProps
        {
            BucketName = $"{cfg.Name(purpose)}-{cfg.Account}",
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            ObjectOwnership = ObjectOwnership.BUCKET_OWNER_ENFORCED,
            RemovalPolicy = purpose == "evidence" ? RemovalPolicy.RETAIN : cfg.Removal,
            AutoDeleteObjects = false,
            EventBridgeEnabled = extra.EventBridgeEnabled,
            LifecycleRules = extra.LifecycleRules,
            Cors = extra.Cors,
            Versioned = extra.Versioned,
            ObjectLockEnabled = extra.ObjectLockEnabled,
            ObjectLockDefaultRetention = extra.ObjectLockDefaultRetention,
        });
}
