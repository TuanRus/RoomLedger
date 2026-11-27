using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.Assertions;
using RoomLedger.Cdk.Stacks;
using Xunit;

namespace RoomLedger.Cdk.Tests;

/**
 * ObservabilityStack (Sang): topic SNS nhận báo động (chưa có người nhận; Phát chọn sau), một dashboard và 14 báo động,
 * tất cả dựa trên tên tài nguyên roomledger-{stage}-… nên không phụ thuộc output của stack khác.
 * Cộng báo động DLQ của WorkflowStack, cả hệ thống có 15 báo động.
 *
 * @author Sang
 * @version 1.0
 */
public class ObservabilityStackTests
{
    private static readonly Lazy<(ObservabilityStack Stack, Template Template)> Dev = new(() => Build("dev"));

    private static (ObservabilityStack Stack, Template Template) Build(string stage)
    {
        var app = new App();
        var stack = new ObservabilityStack(app, $"TestObservability-{stage}", new ObservabilityStackProps
        {
            Stage = stage,
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = "ap-southeast-2" },
        });
        return (stack, Template.FromStack(stack));
    }

    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static IDictionary<string, object> PropsOf(object r) => (IDictionary<string, object>)((IDictionary<string, object>)r)["Properties"];

    private static IDictionary<string, object> Alarm(string name) =>
        Dev.Value.Template.FindResources("AWS::CloudWatch::Alarm").Values.Select(PropsOf)
            .Single(a => a["AlarmName"] as string == name);

    private static string DashboardBody() =>
        Json(PropsOf(Dev.Value.Template.FindResources("AWS::CloudWatch::Dashboard").Values.Single())["DashboardBody"]);

    [Fact]
    public void AlarmTopic_IsNamedForTheStage_AndHasNoRecipientYet()
    {
        Dev.Value.Template.HasResourceProperties("AWS::SNS::Topic", new Dictionary<string, object>
        {
            ["TopicName"] = "roomledger-dev-alarms",
        });
        Dev.Value.Template.ResourceCountIs("AWS::SNS::Subscription", 0);   // người nhận do Phát chọn, thêm sau
        Assert.NotNull(Dev.Value.Stack.AlarmTopic);
    }

    [Fact]
    public void Dashboard_IsNamedForTheStage()
    {
        Dev.Value.Template.HasResourceProperties("AWS::CloudWatch::Dashboard", new Dictionary<string, object>
        {
            ["DashboardName"] = "roomledger-dev",
        });
    }

    [Theory]
    [InlineData("5XXError", "roomledger-dev-api")]          // API Gateway
    [InlineData("Latency", "roomledger-dev-api")]
    [InlineData("CPUUtilization", "roomledger-dev")]        // RDS
    [InlineData("FreeStorageSpace", "roomledger-dev")]
    [InlineData("DatabaseConnections", "roomledger-dev")]
    [InlineData("ApproximateNumberOfMessagesVisible", "roomledger-dev-workflow-dlq")]
    public void Dashboard_ShowsTheKeyMetrics(string metric, string resource)
    {
        var body = DashboardBody();
        Assert.Contains(metric, body);
        Assert.Contains(resource, body);
    }

    [Fact]
    public void Dashboard_ShowsErrorsAndThrottles_ForEveryLambda()
    {
        var body = DashboardBody();
        Assert.Contains("Errors", body);
        Assert.Contains("Throttles", body);
        foreach (var fn in ObservabilityStack.FunctionShortNames)
        {
            Assert.Contains($"roomledger-dev-{fn}", body);
        }
        Assert.Equal(
            ["api", "post-confirmation", "migrate", "photo-ingest", "photo-derive", "evidence-seal",
             "render-pdf", "read-document", "meter-prepare", "meter-persist", "notify"],
            ObservabilityStack.FunctionShortNames);
    }

    [Fact]
    public void Api5xxAlarm_FiresOnFiveServerErrorsInFiveMinutes()
    {
        var a = Alarm("roomledger-dev-api-5xx");
        Assert.Equal("AWS/ApiGateway", a["Namespace"]);
        Assert.Equal("5XXError", a["MetricName"]);
        Assert.Contains("roomledger-dev-api", Json(a["Dimensions"]));
        Assert.Equal("Sum", a["Statistic"]);
        Assert.Equal(300d, Convert.ToDouble(a["Period"]));
        Assert.Equal(5d, Convert.ToDouble(a["Threshold"]));
        Assert.Equal("GreaterThanOrEqualToThreshold", a["ComparisonOperator"]);
    }

    [Fact]
    public void LambdaThrottleAlarm_FiresOnAnyThrottleInTheAccount()
    {
        var a = Alarm("roomledger-dev-lambda-throttles");
        Assert.Equal("AWS/Lambda", a["Namespace"]);
        Assert.Equal("Throttles", a["MetricName"]);
        Assert.False(a.ContainsKey("Dimensions"));   // toàn tài khoản: quota concurrency mới là thứ dễ hết
        Assert.Equal(1d, Convert.ToDouble(a["Threshold"]));
        Assert.Equal("GreaterThanOrEqualToThreshold", a["ComparisonOperator"]);
    }

    [Fact]
    public void RdsStorageAlarm_FiresBelowTwoGigabytes()
    {
        var a = Alarm("roomledger-dev-rds-free-storage");
        Assert.Equal("AWS/RDS", a["Namespace"]);
        Assert.Equal("FreeStorageSpace", a["MetricName"]);
        Assert.Contains("roomledger-dev", Json(a["Dimensions"]));
        Assert.Equal("Minimum", a["Statistic"]);
        Assert.Equal(2d * 1024 * 1024 * 1024, Convert.ToDouble(a["Threshold"]));
        Assert.Equal("LessThanThreshold", a["ComparisonOperator"]);
    }

    [Theory]
    [InlineData("roomledger-dev-api-5xx")]
    [InlineData("roomledger-dev-lambda-throttles")]
    [InlineData("roomledger-dev-rds-free-storage")]
    public void EveryAlarm_NotifiesTheTopic_AndTreatsMissingDataAsOk(string name)
    {
        var a = Alarm(name);
        Assert.Contains("AlarmTopic", Json(a["AlarmActions"]));
        Assert.Equal("notBreaching", a["TreatMissingData"]);
    }

    [Fact]
    public void FourteenAlarmsHere_FifteenInTotalWithTheWorkflowDlqAlarm()
    {
        Dev.Value.Template.ResourceCountIs("AWS::CloudWatch::Alarm", ObservabilityStack.AlarmCount);
        Assert.Equal(14, ObservabilityStack.AlarmCount);
    }

    [Fact]
    public void ApiLatencyAlarm_FiresWhenP90ReachesFiveSeconds()
    {
        var a = Alarm("roomledger-dev-api-latency-p90");
        Assert.Equal("Latency", a["MetricName"]);
        Assert.Equal("p90", a["ExtendedStatistic"]);
        Assert.Equal(5000d, Convert.ToDouble(a["Threshold"]));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("post-confirmation")]
    [InlineData("photo-ingest")]
    [InlineData("photo-derive")]
    [InlineData("evidence-seal")]
    [InlineData("render-pdf")]
    public void EveryLambdaButMigrate_HasAnErrorAlarm(string fn)
    {
        var a = Alarm($"roomledger-dev-{fn}-errors");
        Assert.Equal("AWS/Lambda", a["Namespace"]);
        Assert.Equal("Errors", a["MetricName"]);
        Assert.Contains($"roomledger-dev-{fn}", Json(a["Dimensions"]));
        Assert.Equal("Sum", a["Statistic"]);
        Assert.Equal(1d, Convert.ToDouble(a["Threshold"]));
        Assert.Contains("AlarmTopic", Json(a["AlarmActions"]));
    }

    [Fact]
    public void ErrorAlarms_CoverTheFunctionListMinusMigrateAndTheAiStackLambdas()
    {
        Assert.Equal(
            ObservabilityStack.FunctionShortNames.Where(f => f is not ("migrate" or "read-document" or "meter-prepare" or "meter-persist" or "notify")).OrderBy(f => f),
            ObservabilityStack.AlarmedLambdas.OrderBy(f => f));
    }

    [Fact]
    public void PhotoQueueAgeAlarm_FiresWhenTheOldestMessageWaitsFiveMinutes()
    {
        var a = Alarm("roomledger-dev-photo-queue-age");
        Assert.Equal("AWS/SQS", a["Namespace"]);
        Assert.Equal("ApproximateAgeOfOldestMessage", a["MetricName"]);
        Assert.Contains("roomledger-dev-photo-queue", Json(a["Dimensions"]));
        Assert.Equal(300d, Convert.ToDouble(a["Threshold"]));
        Assert.Contains("AlarmTopic", Json(a["AlarmActions"]));
    }

    [Theory]
    [InlineData("roomledger-dev-rds-cpu", "CPUUtilization", 80d, "GreaterThanOrEqualToThreshold")]
    [InlineData("roomledger-dev-rds-connections", "DatabaseConnections", 60d, "GreaterThanOrEqualToThreshold")]
    [InlineData("roomledger-dev-rds-freeable-memory", "FreeableMemory", 134217728d, "LessThanThreshold")]
    public void RdsAlarms_WatchCpuConnectionsAndMemory(string name, string metric, double threshold, string comparison)
    {
        var a = Alarm(name);
        Assert.Equal("AWS/RDS", a["Namespace"]);
        Assert.Equal(metric, a["MetricName"]);
        Assert.Equal(threshold, Convert.ToDouble(a["Threshold"]));
        Assert.Equal(comparison, a["ComparisonOperator"]);
        Assert.Contains("AlarmTopic", Json(a["AlarmActions"]));
    }

    [Fact]
    public void Prod_UsesProdNames()
    {
        var prod = Build("prod").Template;
        prod.HasResourceProperties("AWS::SNS::Topic", new Dictionary<string, object> { ["TopicName"] = "roomledger-prod-alarms" });
        prod.HasResourceProperties("AWS::CloudWatch::Dashboard", new Dictionary<string, object> { ["DashboardName"] = "roomledger-prod" });
    }
}
