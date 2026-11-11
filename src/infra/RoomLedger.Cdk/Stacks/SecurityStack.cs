using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của SecurityStack: stage được truyền qua props như các stack khác (NetworkStack, DataStack, WorkflowStack).
 *
 * @author Phát
 * @version 1.1
 */
public class SecurityStackProps : StackProps
{
    /** Stage triển khai, chỉ nhận dev hoặc demo. */
    public string Stage { get; set; } = "";
}

/**
 * Bảo mật dùng chung: role deploy cho GitHub Actions qua OIDC (không lưu AWS key trong GitHub).
 * Role chỉ nhận token của đúng repo và nhánh (dev: develop, demo: main), và chỉ được assume ba role bootstrap của CDK:
 * deploy-role, file-publishing-role, lookup-role (đúng account, mọi region). Không có image-publishing-role vì chưa dùng image asset.
 *
 * OIDC provider: mỗi account chỉ có một provider cho mỗi URL, nên mặc định stack KHÔNG tạo provider mà tham chiếu
 * provider chuẩn của account (ARN cố định theo account). Hai cách tạo provider, chỉ làm MỘT lần cho cả account:
 * (1) tạo tay bằng AWS CLI (create-open-id-connect-provider, url https://token.actions.githubusercontent.com,
 * client-id sts.amazonaws.com); hoặc (2) deploy đúng một stage kèm -c createGithubOidcProvider=true
 * (provider được giữ lại khi xóa stack, nên cdk destroy không làm hỏng stage còn lại).
 * Provider phải tồn tại trước khi deploy stack này.
 *
 * Cấu hình qua context (-c), đều có mặc định: githubRepo (TuanRus/RoomLedger); githubSubjects (danh sách sub chính xác,
 * cách nhau bằng dấu phẩy; cần đổi nếu dùng GitHub Environment, khi đó sub có dạng repo:{repo}:environment:{tên});
 * cdkQualifier (hnb659fds); createGithubOidcProvider (false).
 *
 * KMS, CloudTrail, GuardDuty, Budgets, Secrets Manager sẽ được thêm ở các PR sau.
 *
 * @author Phát
 * @version 1.1
 */
public class SecurityStack : Stack
{
    private const string GitHubIssuer = "token.actions.githubusercontent.com";

    private static readonly Dictionary<string, string> BranchByStage = new()
    {
        ["dev"] = "develop",
        ["demo"] = "main",
    };

    /** Role mà GitHub Actions assume bằng OIDC (aws-actions/configure-aws-credentials, role-to-assume). */
    public Role GitHubDeployRole { get; }

    public SecurityStack(Construct scope, string id, SecurityStackProps props) : base(scope, id, props)
    {
        var stage = props.Stage;
        if (!BranchByStage.TryGetValue(stage, out var defaultBranch))
        {
            // Không đoán nhánh cho stage lạ (ví dụ Dev): sai chữ hoa thường sẽ làm role trỏ nhầm nhánh.
            throw new ArgumentException(
                $"Stage '{stage}' không hợp lệ, chỉ nhận: {string.Join(", ", BranchByStage.Keys)}.", nameof(props));
        }

        var repo = ContextValue("githubRepo") ?? "TuanRus/RoomLedger";
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
            RoleName = $"roomledger-{stage}-github-deploy",
            Description = "Role cho GitHub Actions deploy RoomLedger qua OIDC; chỉ assume được các role bootstrap của CDK",
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

        _ = new CfnOutput(this, "GitHubDeployRoleArn", new CfnOutputProps { Value = GitHubDeployRole.RoleArn });
    }

    private string? ContextValue(string key) => Node.TryGetContext(key) as string;

    private bool ContextFlag(string key) =>
        string.Equals(Node.TryGetContext(key)?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
}
