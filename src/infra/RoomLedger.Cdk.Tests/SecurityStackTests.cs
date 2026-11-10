using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

using RoomLedger.Cdk.Stacks;

namespace RoomLedger.Cdk.Tests;

/// <summary>Bảo mật PR 1 — SecurityStack: OIDC provider và role deploy cho GitHub Actions.</summary>
public class SecurityStackTests
{
    private const string Region = "ap-southeast-2";
    private const string Issuer = "token.actions.githubusercontent.com";

    private static Template Build(Dictionary<string, object>? context = null)
    {
        var app = new App(new AppProps { Context = context ?? new Dictionary<string, object>() });
        var stack = new SecurityStack(app, "TestSecurity", new StackProps
        {
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = Region },
        });
        return Template.FromStack(stack);
    }

    private static string Json(Template t) => JsonSerializer.Serialize(t.ToJSON());

    [Fact]
    public void Creates_github_oidc_provider_with_sts_audience()
        => Build().HasResourceProperties("AWS::IAM::OIDCProvider", new Dictionary<string, object>
        {
            ["Url"] = $"https://{Issuer}",
            ["ClientIdList"] = new object[] { "sts.amazonaws.com" },
        });

    [Fact]
    public void Reuses_existing_provider_when_arn_is_given()
    {
        const string arn = "arn:aws:iam::123456789012:oidc-provider/token.actions.githubusercontent.com";
        var t = Build(new Dictionary<string, object> { ["githubOidcProviderArn"] = arn });
        t.ResourceCountIs("AWS::IAM::OIDCProvider", 0);
        Assert.Contains(arn, Json(t));
    }

    [Fact]
    public void Trust_policy_allows_only_web_identity_from_the_exact_repo_and_branch()
        => Build().HasResourceProperties("AWS::IAM::Role", new Dictionary<string, object>
        {
            ["AssumeRolePolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Action"] = "sts:AssumeRoleWithWebIdentity",
                        ["Effect"] = "Allow",
                        ["Condition"] = new Dictionary<string, object>
                        {
                            ["StringEquals"] = new Dictionary<string, object>
                            {
                                [$"{Issuer}:aud"] = "sts.amazonaws.com",
                                [$"{Issuer}:sub"] = "repo:TuanRus/RoomLedger:ref:refs/heads/develop",
                            },
                        },
                    }),
                }),
            }),
        });

    [Fact]
    public void Demo_stage_trusts_main_branch_only()
    {
        var json = Json(Build(new Dictionary<string, object> { ["stage"] = "demo" }));
        Assert.Contains("repo:TuanRus/RoomLedger:ref:refs/heads/main", json);
        Assert.DoesNotContain("refs/heads/develop", json);
    }

    [Theory]
    [InlineData("repo:TuanRus/RoomLedger:*")]
    [InlineData("repo:TuanRus/RoomLedger:ref:refs/heads/*")]
    [InlineData("repo:Someone/Else:ref:refs/heads/develop")]
    public void Rejects_subjects_with_wildcards_or_from_another_repo(string subject)
        => Assert.Throws<ArgumentException>(
            () => Build(new Dictionary<string, object> { ["githubSubjects"] = subject }));

    [Fact]
    public void Role_can_only_assume_cdk_bootstrap_roles()
    {
        var t = Build();
        t.HasResourceProperties("AWS::IAM::Policy", new Dictionary<string, object>
        {
            ["PolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["Statement"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["Action"] = "sts:AssumeRole",
                        ["Effect"] = "Allow",
                    }),
                }),
            }),
        });
        Assert.Contains("role/cdk-hnb659fds-*", Json(t));
    }

    [Fact]
    public void Has_no_wildcard_conditions_actions_or_resources()
    {
        var json = Json(Build());
        Assert.DoesNotContain("StringLike", json);
        Assert.DoesNotContain("\"Action\":\"*\"", json);
        Assert.DoesNotContain("\"Resource\":\"*\"", json);
    }

    [Fact]
    public void Session_lasts_one_hour()
        => Build().HasResourceProperties("AWS::IAM::Role", new Dictionary<string, object>
        {
            ["MaxSessionDuration"] = 3600,
        });
}
