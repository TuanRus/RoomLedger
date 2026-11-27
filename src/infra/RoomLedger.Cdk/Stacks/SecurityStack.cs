using Amazon.CDK;
using Amazon.CDK.AWS.Budgets;
using Amazon.CDK.AWS.CloudTrail;
using Amazon.CDK.AWS.GuardDuty;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của SecurityStack: stage được truyền qua props như các stack khác (NetworkStack, DataStack, WorkflowStack).
 *
 * @author Phát
 * @version 1.2
 */
public class SecurityStackProps : StackProps
{
    /** Stage triển khai, chỉ nhận stage có trong githubBranches (dev, demo, prod). */
    public string Stage { get; set; } = "";

    /** Tên bucket evidence (cùng tên Program.cs truyền cho StorageStack), CloudTrail ghi data events của bucket này. */
    public string EvidenceBucketName { get; set; } = "";
}

/**
 * Bảo mật dùng chung: role deploy cho GitHub Actions qua OIDC (không lưu AWS key trong GitHub).
 * Role chỉ nhận token của đúng repo và nhánh (theo githubBranches trong cdk.json), và chỉ được assume ba role bootstrap của CDK:
 * deploy-role, file-publishing-role, lookup-role (đúng account, mọi region). Không có image-publishing-role vì chưa dùng image asset.
 *
 * OIDC provider: mỗi account chỉ có một provider cho mỗi URL, nên mặc định stack KHÔNG tạo provider mà tham chiếu
 * provider chuẩn của account (ARN cố định theo account). Hai cách tạo provider, chỉ làm MỘT lần cho cả account:
 * (1) tạo tay bằng AWS CLI (create-open-id-connect-provider, url https://token.actions.githubusercontent.com,
 * client-id sts.amazonaws.com); hoặc (2) deploy đúng một stage kèm -c createGithubOidcProvider=true
 * (provider được giữ lại khi xóa stack, nên cdk destroy không làm hỏng stage còn lại).
 * Provider phải tồn tại trước khi deploy stack này.
 *
 * Cấu hình qua context (cdk.json hoặc -c). Bắt buộc: githubRepo (owner/repo); githubBranches (object stage → nhánh,
 * ví dụ { "dev": "develop", "demo": "main" }; stage không có trong object sẽ bị từ chối).
 * Tùy chọn: githubSubjects (danh sách sub chính xác, cách nhau bằng dấu phẩy; cần đổi nếu dùng GitHub Environment,
 * khi đó sub có dạng repo:{repo}:environment:{tên}); cdkQualifier (hnb659fds); createGithubOidcProvider (false).
 *
 * Rủi ro còn lại (ghi vào threat model): deploy-role cho CloudFormation chạy bằng cfn-exec-role, mà `cdk bootstrap`
 * mặc định gắn AdministratorAccess. Nên quyền thực tế của pipeline vẫn gần như admin trong account, dù role GitHub
 * chỉ assume được ba role trên. Giảm rủi ro: chỉ nhánh develop/main lấy được token (đã có); bootstrap kèm
 * --cloudformation-execution-policies trỏ tới policy hẹp hơn (chỉ dịch vụ các stack dùng) khi danh sách dịch vụ đã ổn định.
 *
 * CloudTrail ghi S3 data events của bucket evidence (ai đọc/ghi/xóa ảnh bằng chứng). KMS key của evidence nằm ở StorageStack.
 *
 * @author Phát
 * @version 1.2
 */
public class SecurityStack : Stack
{
    private const string GitHubIssuer = "token.actions.githubusercontent.com";

    /** Role mà GitHub Actions assume bằng OIDC (aws-actions/configure-aws-credentials, role-to-assume). */
    public Role GitHubDeployRole { get; }

    public SecurityStack(Construct scope, string id, SecurityStackProps props) : base(scope, id, props)
    {
        var stage = props.Stage;
        var branchByStage = BranchesFromContext();
        if (!branchByStage.TryGetValue(stage, out var defaultBranch))
        {
            // Không đoán nhánh cho stage lạ (ví dụ Dev): sai chữ hoa thường sẽ làm role trỏ nhầm nhánh.
            throw new ArgumentException(
                $"Stage '{stage}' không hợp lệ, chỉ nhận: {string.Join(", ", branchByStage.Keys)}.", nameof(props));
        }

        var repo = ContextValue("githubRepo")
            ?? throw new ArgumentException("Thiếu context githubRepo (owner/repo) trong cdk.json.", nameof(props));
        var qualifier = ContextValue("cdkQualifier") ?? "hnb659fds";
        var subjects = (ContextValue("githubSubjects") ?? $"repo:{repo}:ref:refs/heads/{defaultBranch}")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Sub phải chính xác và thuộc đúng repo: ký tự đại diện hoặc repo khác sẽ cho phép workflow ngoài ý muốn assume role.
        if (subjects.Length == 0 ||
            subjects.Any(s => s.Contains('*') || s.Contains('?') || !s.StartsWith($"repo:{repo}:", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"githubSubjects phải là các sub chính xác của repo {repo} (dạng repo:{repo}:ref:refs/heads/<nhánh>), không dùng ký tự đại diện.");
        }

        string providerArn;
        if (ContextFlag("createGithubOidcProvider"))
        {
            var provider = new CfnOIDCProvider(this, "GitHubOidcProvider", new CfnOIDCProviderProps
            {
                Url = $"https://{GitHubIssuer}",
                ClientIdList = new[] { "sts.amazonaws.com" },
            });
            provider.ApplyRemovalPolicy(RemovalPolicy.RETAIN);
            providerArn = provider.AttrArn;
        }
        else
        {
            providerArn = $"arn:{Aws.PARTITION}:iam::{Account}:oidc-provider/{GitHubIssuer}";
        }

        GitHubDeployRole = new Role(this, "GitHubDeployRole", new RoleProps
        {
            RoleName = Naming.Resource(stage, "github-deploy"),
            // Description đi thẳng lên IAM, chỉ nhận ASCII hoặc Latin-1: không dùng tiếng Việt có dấu ở đây.
            Description = "GitHub Actions deploy role for RoomLedger via OIDC; can only assume the CDK bootstrap roles",
            MaxSessionDuration = Duration.Hours(1),
            AssumedBy = new FederatedPrincipal(providerArn, new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object>
                {
                    [$"{GitHubIssuer}:aud"] = "sts.amazonaws.com",
                    [$"{GitHubIssuer}:sub"] = subjects.Length == 1 ? (object)subjects[0] : subjects,
                },
            }, "sts:AssumeRoleWithWebIdentity"),
        });

        // Chỉ ba role bootstrap cần cho deploy. cfn-exec-role do CloudFormation dùng, không cho GitHub assume trực tiếp.
        var bootstrapRoleArns = new[] { "deploy-role", "file-publishing-role", "lookup-role" }
            .Select(name => $"arn:{Aws.PARTITION}:iam::{Account}:role/cdk-{qualifier}-{name}-{Account}-*")
            .ToArray();
        GitHubDeployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AssumeCdkBootstrapRoles",
            Effect = Effect.ALLOW,
            Actions = new[] { "sts:AssumeRole" },
            Resources = bootstrapRoleArns,
        }));

        AddEvidenceTrail(stage, props.EvidenceBucketName);
        AddGuardDuty();
        AddBudget(stage);

        _ = new CfnOutput(this, "GitHubDeployRoleArn", new CfnOutputProps { Value = GitHubDeployRole.RoleArn });
    }

    /**
     * Trail một region, ghi management events (bản đầu tiên miễn phí) và S3 data events (đọc + ghi) của bucket evidence.
     * Log vào bucket riêng (SSE-S3, chặn truy cập công khai, TLS), xóa sau 90 ngày, bật xác thực file log.
     * Không gửi sang CloudWatch Logs để tránh phí ingest.
     */
    private void AddEvidenceTrail(string stage, string evidenceBucketName)
    {
        if (string.IsNullOrWhiteSpace(evidenceBucketName))
        {
            throw new ArgumentException("Thiếu EvidenceBucketName: CloudTrail cần biết bucket evidence để ghi data events.");
        }

        var isDev = stage == "dev";
        var logBucket = new Bucket(this, "TrailLogBucket", new BucketProps
        {
            BucketName = $"{Naming.Resource(stage, "trail-logs")}-{Account}",
            BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
            Encryption = BucketEncryption.S3_MANAGED,
            EnforceSSL = true,
            ObjectOwnership = ObjectOwnership.BUCKET_OWNER_ENFORCED,
            LifecycleRules = [new LifecycleRule { Id = "expire-90-days", Expiration = Duration.Days(90) }],
            RemovalPolicy = isDev ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN,
            AutoDeleteObjects = isDev,
        });

        var trail = new Trail(this, "EvidenceTrail", new TrailProps
        {
            TrailName = Naming.Resource(stage, "trail"),
            Bucket = logBucket,
            EnableFileValidation = true,
            IncludeGlobalServiceEvents = false,
            IsMultiRegionTrail = false,
            SendToCloudWatchLogs = false,
        });
        trail.AddS3EventSelector(
            [new S3EventSelector { Bucket = Bucket.FromBucketName(this, "EvidenceBucketRef", evidenceBucketName) }],
            new AddEventSelectorOptions { ReadWriteType = ReadWriteType.ALL });
    }

    /**
     * GuardDuty với S3 Protection (phát hiện truy cập bất thường vào S3 từ CloudTrail data events).
     * Mỗi account + region chỉ có một detector: nếu account đã bật GuardDuty thì deploy với -c createGuardDutyDetector=false
     * (rồi bật S3 Protection trên detector sẵn có bằng tay).
     */
    private void AddGuardDuty()
    {
        if (string.Equals(ContextValue("createGuardDutyDetector"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = new CfnDetector(this, "GuardDutyDetector", new CfnDetectorProps
        {
            Enable = true,
            // Thuộc tính kiểu object nên phải là mảng tường minh, không dùng collection expression
            Features = new[]
            {
                new CfnDetector.CFNFeatureConfigurationProperty { Name = "S3_DATA_EVENTS", Status = "ENABLED" },
            },
        });
    }

    /**
     * AWS Budgets: ngân sách chi phí hằng tháng (mặc định 100 USD, đổi bằng -c monthlyBudgetUsd=...), cảnh báo khi chi thực tế
     * đạt 20% / 50% / 80% và khi dự báo vượt 100%. Cảnh báo vào topic SNS riêng (Budgets cần quyền publish);
     * muốn nhận email thì truyền -c budgetAlertEmail=... (không ghi email trong code).
     */
    private void AddBudget(string stage)
    {
        var limit = double.TryParse(ContextValue("monthlyBudgetUsd"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 100d;

        var topic = new Topic(this, "BudgetTopic", new TopicProps
        {
            TopicName = Naming.Resource(stage, "budget-alerts"),
            DisplayName = "RoomLedger budget alerts",
        });
        topic.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AllowBudgetsToPublish",
            Effect = Effect.ALLOW,
            Principals = [new ServicePrincipal("budgets.amazonaws.com")],
            Actions = ["sns:Publish"],
            Resources = [topic.TopicArn],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object> { ["aws:SourceAccount"] = Account },
            },
        }));
        if (ContextValue("budgetAlertEmail") is { } email)
        {
            topic.AddSubscription(new EmailSubscription(email));
        }

        CfnBudget.NotificationWithSubscribersProperty Notify(string type, double threshold) => new()
        {
            Notification = new CfnBudget.NotificationProperty
            {
                NotificationType = type,
                ComparisonOperator = "GREATER_THAN",
                Threshold = threshold,
                ThresholdType = "PERCENTAGE",
            },
            Subscribers = new[] { new CfnBudget.SubscriberProperty { SubscriptionType = "SNS", Address = topic.TopicArn } },
        };

        _ = new CfnBudget(this, "MonthlyBudget", new CfnBudgetProps
        {
            Budget = new CfnBudget.BudgetDataProperty
            {
                BudgetName = Naming.Resource(stage, "monthly"),
                BudgetType = "COST",
                TimeUnit = "MONTHLY",
                BudgetLimit = new CfnBudget.SpendProperty { Amount = limit, Unit = "USD" },
            },
            NotificationsWithSubscribers = new[] { Notify("ACTUAL", 20), Notify("ACTUAL", 50), Notify("ACTUAL", 80), Notify("FORECASTED", 100) },
        });
    }

    private string? ContextValue(string key) => Node.TryGetContext(key) as string is { Length: > 0 } v ? v : null;

    /** Đọc githubBranches (object stage → nhánh) từ context; so khớp stage phân biệt hoa thường. */
    private Dictionary<string, string> BranchesFromContext()
    {
        if (Node.TryGetContext("githubBranches") is not IDictionary<string, object> raw || raw.Count == 0)
        {
            throw new ArgumentException(
                "Thiếu context githubBranches trong cdk.json, dạng { \"dev\": \"develop\", \"demo\": \"main\" }.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (stage, branch) in raw)
        {
            if (branch is not string name || string.IsNullOrWhiteSpace(name) || name.Contains('*') || name.Contains('?'))
            {
                throw new ArgumentException($"githubBranches.{stage} phải là tên nhánh cụ thể, không dùng ký tự đại diện.");
            }

            result[stage] = name;
        }

        return result;
    }

    private bool ContextFlag(string key) =>
        string.Equals(Node.TryGetContext(key)?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
}
