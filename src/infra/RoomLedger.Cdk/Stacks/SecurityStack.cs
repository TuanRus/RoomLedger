using Amazon.CDK;
using Amazon.CDK.AWS.IAM;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Bảo mật dùng chung: OIDC provider của GitHub Actions và role deploy cho pipeline (không lưu AWS key trong GitHub).
 * Role chỉ nhận token của đúng repo và nhánh, và chỉ được assume các role bootstrap của CDK (cdk-{qualifier}-*).
 * KMS, CloudTrail, GuardDuty, Budgets, Secrets Manager sẽ được thêm ở các PR sau.
 *
 * Cấu hình qua context (-c), đều có mặc định:
 * githubRepo (TuanRus/RoomLedger); githubSubjects (danh sách sub chính xác, cách nhau bằng dấu phẩy,
 * mặc định là nhánh develop cho stage dev và main cho stage khác); githubOidcProviderArn (dùng provider
 * có sẵn trong account, vì mỗi account chỉ có một provider cho mỗi URL); cdkQualifier (hnb659fds).
 *
 * @author Phát
 * @version 1.0
 */
public class SecurityStack : Stack
{
    private const string GitHubIssuer = "token.actions.githubusercontent.com";

    /** Role mà GitHub Actions assume bằng OIDC (aws-actions/configure-aws-credentials, role-to-assume). */
    public Role GitHubDeployRole { get; }

    public SecurityStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
        var stage = ContextValue("stage") ?? "dev";
        var repo = ContextValue("githubRepo") ?? "TuanRus/RoomLedger";
        var qualifier = ContextValue("cdkQualifier") ?? "hnb659fds";
        var defaultBranch = stage == "dev" ? "develop" : "main";
        var subjects = (ContextValue("githubSubjects") ?? $"repo:{repo}:ref:refs/heads/{defaultBranch}")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Sub phải chính xác và thuộc đúng repo: ký tự đại diện hoặc repo khác sẽ cho phép workflow ngoài ý muốn assume role.
        if (subjects.Length == 0 ||
            subjects.Any(s => s.Contains('*') || s.Contains('?') || !s.StartsWith($"repo:{repo}:", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"githubSubjects phải là các sub chính xác của repo {repo} (dạng repo:{repo}:ref:refs/heads/<nhánh>), không dùng ký tự đại diện.");
        }

        // Mỗi account chỉ có một OIDC provider cho mỗi URL: nếu đã có thì truyền ARN qua -c githubOidcProviderArn=...
        var providerArn = ContextValue("githubOidcProviderArn")
            ?? new CfnOIDCProvider(this, "GitHubOidcProvider", new CfnOIDCProviderProps
            {
                Url = $"https://{GitHubIssuer}",
                ClientIdList = new[] { "sts.amazonaws.com" },
            }).AttrArn;

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

        // Deploy thật do các role bootstrap của CDK đảm nhiệm; role này chỉ được phép assume chúng.
        GitHubDeployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "AssumeCdkBootstrapRoles",
            Effect = Effect.ALLOW,
            Actions = new[] { "sts:AssumeRole" },
            Resources = new[] { $"arn:{Aws.PARTITION}:iam::{Account}:role/cdk-{qualifier}-*" },
        }));

        _ = new CfnOutput(this, "GitHubDeployRoleArn", new CfnOutputProps { Value = GitHubDeployRole.RoleArn });
    }

    private string? ContextValue(string key) => Node.TryGetContext(key) as string;
}
