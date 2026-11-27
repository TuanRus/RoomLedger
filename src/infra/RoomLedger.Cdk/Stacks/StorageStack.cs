using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.KMS;
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
    public required string OutboxBucketName { get; init; }
    /**
     * Origin của web cho CORS PUT vào bucket incoming, ví dụ https://demo.example.com.
     * Bắt buộc ở stage khác dev (không được bỏ trống hay "*"); dev bỏ trống thì dùng "*".
     */
    public string? WebOrigin { get; init; }
}

/**
 * 4 bucket incoming / outbox (sự kiện, xóa sau 7 ngày) / evidence (Object Lock) / derived, lifecycle, chặn truy cập công khai, bắt buộc TLS.
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
    public Bucket Outbox { get; }
    /** Khóa KMS (CMK) duy nhất của hệ thống, mã hóa bucket evidence (SSE-KMS). */
    public Key EvidenceKey { get; }

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

        // dev: giữ 1 ngày để dọn được nhanh, không tiering (ảnh test, tránh phí giám sát).
        // demo/prod: 365 ngày, S3 Intelligent-Tiering tự chuyển lớp theo tần suất truy cập (không phí truy xuất, không tối thiểu lưu trữ như Glacier).
        // Đổi retention sau không áp lại cho object đã ghi.
        EvidenceKey = NewEvidenceKey(props.Stage);

        Evidence = NewBucket("Evidence", props.EvidenceBucketName, RemovalPolicy.RETAIN, new BucketProps
        {
            EncryptionKey = EvidenceKey,
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
                        Id = "evidence-intelligent-tiering",
                        Transitions = [new Transition { StorageClass = StorageClass.INTELLIGENT_TIERING, TransitionAfter = Duration.Days(0) }],
                    },
                },
        });

        Derived = NewBucket("Derived", props.DerivedBucketName, isDev ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN, new BucketProps());

        // Outbox sự kiện: Lambda ghi qua Gateway endpoint, bucket đẩy "Object Created" lên EventBridge
        Outbox = NewBucket("Outbox", props.OutboxBucketName, isDev ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN, new BucketProps
        {
            EventBridgeEnabled = true,
            LifecycleRules = [new LifecycleRule { Id = "expire-7-days", Expiration = Duration.Days(7) }],
        });

        new CfnOutput(this, "EvidenceKeyArn", new CfnOutputProps { Value = EvidenceKey.KeyArn });
        new CfnOutput(this, "OutboxBucket", new CfnOutputProps { Value = Outbox.BucketName });
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

    /**
     * CMK cho evidence: bật xoay khóa hằng năm, luôn RETAIN (bucket evidence là RETAIN; xóa khóa thì ảnh không đọc lại được).
     * Ngoài chính sách mặc định (tin cậy tài khoản), cho mọi principal trong tài khoản dùng khóa nhưng CHỈ qua S3
     * (kms:ViaService + kms:CallerAccount); quyền thật vẫn do IAM của từng Lambda quyết định.
     * Nhờ vậy Lambda của WorkflowStack (khai báo quyền S3 theo ARN) ghi và đọc evidence mà không cần sửa stack đó.
     */
    private Key NewEvidenceKey(string stage)
    {
        var key = new Key(this, "EvidenceKey", new KeyProps
        {
            Alias = $"alias/{Naming.Resource(stage, "evidence")}",
            Description = "RoomLedger evidence bucket encryption key",
            EnableKeyRotation = true,
            RemovalPolicy = RemovalPolicy.RETAIN,
        });
        key.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowAccountPrincipalsViaS3Only",
            Effect = Effect.ALLOW,
            Principals = [new AnyPrincipal()],
            Actions = ["kms:Decrypt", "kms:GenerateDataKey", "kms:DescribeKey"],
            Resources = ["*"],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object>
                {
                    ["kms:ViaService"] = $"s3.{Region}.amazonaws.com",
                    ["kms:CallerAccount"] = Account,
                },
            },
        }));
        return key;
    }

    private Bucket NewBucket(string id, string name, RemovalPolicy removal, BucketProps extra) =>
        new(this, id, new BucketProps
        {
            BucketName = name,
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            // Có khóa KMS thì dùng SSE-KMS kèm S3 Bucket Key (giảm số lần gọi KMS); không thì SSE-S3
            Encryption = extra.EncryptionKey is null ? BucketEncryption.S3_MANAGED : BucketEncryption.KMS,
            EncryptionKey = extra.EncryptionKey,
            BucketKeyEnabled = extra.EncryptionKey is not null,
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
