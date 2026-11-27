using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

using RoomLedger.Cdk.Stacks;

namespace RoomLedger.Cdk.Tests;

/**
 * Kiểm thử SecurityStack: role deploy cho GitHub Actions qua OIDC, tham chiếu provider dùng chung,
 * giới hạn nhánh theo stage và phạm vi assume role.
 *
 * @author Phát
 * @version 1.2
 */
public class SecurityStackTests
{
    private const string Region = "ap-southeast-2";
    private const string Issuer = "token.actions.githubusercontent.com";

    /** Giống khối context trong cdk.json; test truyền thêm key để ghi đè, giá trị null để bỏ key. */
    private static Dictionary<string, object> BaseContext() => new()
    {
        ["githubRepo"] = "TuanRus/RoomLedger",
        ["githubBranches"] = new Dictionary<string, object> { ["dev"] = "develop", ["demo"] = "main" },
    };

    private static Template Build(string stage = "dev", Dictionary<string, object?>? context = null)
    {
        var merged = BaseContext();
        foreach (var (key, value) in context ?? new Dictionary<string, object?>())
        {
            if (value is null) merged.Remove(key); else merged[key] = value;
        }

        var app = new App(new AppProps { Context = merged });
        var stack = new SecurityStack(app, "TestSecurity", new SecurityStackProps
        {
            Stage = stage,
            EvidenceBucketName = $"roomledger-{stage}-evidence",
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = Region },
        });
        return Template.FromStack(stack);
    }

    private static string Json(Template t) => JsonSerializer.Serialize(t.ToJSON());

    [Fact]
    public void References_account_oidc_provider_without_creating_one_by_default()
    {
        var t = Build();
        t.ResourceCountIs("AWS::IAM::OIDCProvider", 0);
        var json = Json(t);
        Assert.Contains("oidc-provider/token.actions.githubusercontent.com", json);
        Assert.Contains("123456789012", json);
    }

    [Fact]
    public void Creates_and_retains_provider_only_when_flag_is_set()
    {
        var t = Build(context: new Dictionary<string, object?> { ["createGithubOidcProvider"] = "true" });
        t.HasResourceProperties("AWS::IAM::OIDCProvider", new Dictionary<string, object>
        {
            ["Url"] = $"https://{Issuer}",
            ["ClientIdList"] = new object[] { "sts.amazonaws.com" },
        });
        t.HasResource("AWS::IAM::OIDCProvider", new Dictionary<string, object>
        {
            ["DeletionPolicy"] = "Retain",
        });
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
        var json = Json(Build("demo"));
        Assert.Contains("repo:TuanRus/RoomLedger:ref:refs/heads/main", json);
        Assert.DoesNotContain("refs/heads/develop", json);
    }

    [Fact]
    public void Prod_stage_from_cdk_json_trusts_main_branch_only()
    {
        var json = Json(Build("prod", new Dictionary<string, object?>
        {
            ["githubBranches"] = new Dictionary<string, object> { ["dev"] = "develop", ["demo"] = "main", ["prod"] = "main" },
        }));
        Assert.Contains("repo:TuanRus/RoomLedger:ref:refs/heads/main", json);
        Assert.DoesNotContain("refs/heads/develop", json);
    }

    [Theory]
    [InlineData("Dev")]
    [InlineData("prod")]
    [InlineData("")]
    public void Rejects_unknown_stage_instead_of_guessing_a_branch(string stage)
        => Assert.Throws<ArgumentException>(() => Build(stage));

    [Theory]
    [InlineData("githubRepo")]
    [InlineData("githubBranches")]
    public void Requires_repo_and_branch_mapping_from_context_instead_of_hard_coded_defaults(string key)
        => Assert.Throws<ArgumentException>(() => Build(context: new Dictionary<string, object?> { [key] = null }));

    [Fact]
    public void Branch_mapping_comes_from_context()
    {
        var json = Json(Build(context: new Dictionary<string, object?>
        {
            ["githubBranches"] = new Dictionary<string, object> { ["dev"] = "feature-x", ["demo"] = "main" },
        }));
        Assert.Contains("repo:TuanRus/RoomLedger:ref:refs/heads/feature-x", json);
        Assert.DoesNotContain("refs/heads/develop", json);
    }

    [Theory]
    [InlineData("repo:TuanRus/RoomLedger:*")]
    [InlineData("repo:TuanRus/RoomLedger:ref:refs/heads/*")]
    [InlineData("repo:Someone/Else:ref:refs/heads/develop")]
    public void Rejects_subjects_with_wildcards_or_from_another_repo(string subject)
        => Assert.Throws<ArgumentException>(
            () => Build(context: new Dictionary<string, object?> { ["githubSubjects"] = subject }));

    [Fact]
    public void Role_can_only_assume_deploy_file_publishing_and_lookup_roles_of_this_account()
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

        var json = Json(t);
        Assert.Contains("role/cdk-hnb659fds-deploy-role-123456789012-*", json);
        Assert.Contains("role/cdk-hnb659fds-file-publishing-role-123456789012-*", json);
        Assert.Contains("role/cdk-hnb659fds-lookup-role-123456789012-*", json);
        Assert.DoesNotContain("role/cdk-hnb659fds-*", json);
        Assert.DoesNotContain("cfn-exec-role", json);
        Assert.DoesNotContain("image-publishing-role", json);
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

    [Fact]
    public void Template_has_no_characters_beyond_latin1_because_iam_rejects_them_in_descriptions()
    {
        // IAM chỉ nhận Description theo mẫu [\u0009\u000A\u000D\u0020-\u007E\u00A1-\u00FF]; System.Text.Json thoát ký tự ngoài ASCII thành \uXXXX.
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"\\u(?!00)[0-9a-fA-F]{4}"), Json(Build()));
    }

    [Fact]
    public void Trail_RecordsS3DataEventsOfTheEvidenceBucket_WithFileValidation()
    {
        var t = Build();
        t.ResourceCountIs("AWS::CloudTrail::Trail", 1);
        t.HasResourceProperties("AWS::CloudTrail::Trail", new Dictionary<string, object>
        {
            ["TrailName"] = "roomledger-dev-trail",
            ["IsLogging"] = true,
            ["EnableLogFileValidation"] = true,
            ["IsMultiRegionTrail"] = false,
        });
        var json = Json(t);
        Assert.Contains("AWS::S3::Object", json);
        Assert.Contains("roomledger-dev-evidence", json);
    }

    [Fact]
    public void TrailLogBucket_IsPrivateEncryptedAndExpires()
    {
        var t = Build();
        t.HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
        {
            ["BucketName"] = Match.StringLikeRegexp("^roomledger-dev-trail-logs-"),
            ["PublicAccessBlockConfiguration"] = Match.ObjectLike(new Dictionary<string, object> { ["BlockPublicAcls"] = true }),
        });
        Assert.Contains("expire-90-days", Json(t));
    }

    [Fact]
    public void Trail_WithoutEvidenceBucketName_FailsAtSynth()
    {
        var app = new App(new AppProps { Context = BaseContext() });
        Assert.Throws<ArgumentException>(() => new SecurityStack(app, "NoEvidence", new SecurityStackProps
        {
            Stage = "dev",
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = Region },
        }));
    }

    [Fact]
    public void GuardDuty_IsEnabled_WithS3Protection()
    {
        var t = Build();
        t.ResourceCountIs("AWS::GuardDuty::Detector", 1);
        t.HasResourceProperties("AWS::GuardDuty::Detector", new Dictionary<string, object>
        {
            ["Enable"] = true,
            ["Features"] = new object[]
            {
                Match.ObjectLike(new Dictionary<string, object> { ["Name"] = "S3_DATA_EVENTS", ["Status"] = "ENABLED" }),
            },
        });
    }

    [Fact]
    public void GuardDuty_CanBeSkipped_WhenTheAccountAlreadyHasADetector()
    {
        var t = Build(context: new Dictionary<string, object?> { ["createGuardDutyDetector"] = "false" });
        t.ResourceCountIs("AWS::GuardDuty::Detector", 0);
    }

    [Fact]
    public void Budget_Defaults100UsdMonthly_WithThreeActualAlertsAndOneForecast()
    {
        var t = Build();
        t.ResourceCountIs("AWS::Budgets::Budget", 1);
        t.HasResourceProperties("AWS::Budgets::Budget", new Dictionary<string, object>
        {
            ["Budget"] = Match.ObjectLike(new Dictionary<string, object>
            {
                ["BudgetName"] = "roomledger-dev-monthly",
                ["BudgetType"] = "COST",
                ["TimeUnit"] = "MONTHLY",
                ["BudgetLimit"] = new Dictionary<string, object> { ["Amount"] = 100, ["Unit"] = "USD" },
            }),
        });
        var json = Json(t);
        foreach (var threshold in new[] { "20", "50", "80", "100" })
        {
            Assert.Contains($"\"Threshold\":{threshold}", json);
        }
        Assert.Contains("FORECASTED", json);
    }

    [Fact]
    public void Budget_AmountComesFromContext()
    {
        var t = Build(context: new Dictionary<string, object?> { ["monthlyBudgetUsd"] = "150" });
        Assert.Contains("\"Amount\":150", Json(t));
    }

    [Fact]
    public void Budget_AlertsGoToATopicThatBudgetsMayPublishTo_AndHasNoEmailByDefault()
    {
        var t = Build();
        t.HasResourceProperties("AWS::SNS::Topic", new Dictionary<string, object> { ["TopicName"] = "roomledger-dev-budget-alerts" });
        Assert.Contains("budgets.amazonaws.com", Json(t));
        t.ResourceCountIs("AWS::SNS::Subscription", 0);
    }

    [Fact]
    public void Budget_EmailSubscription_IsAddedOnlyWhenGivenByContext()
    {
        var t = Build(context: new Dictionary<string, object?> { ["budgetAlertEmail"] = "alerts@example.com" });
        t.HasResourceProperties("AWS::SNS::Subscription", new Dictionary<string, object>
        {
            ["Protocol"] = "email",
            ["Endpoint"] = "alerts@example.com",
        });
    }
}
