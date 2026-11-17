using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.SNS;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của ObservabilityStack.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class ObservabilityStackProps : StackProps
{
    public required string Stage { get; init; }
}

/**
 * Giám sát RoomLedger (Sang). Mọi chỉ số lấy theo tên tài nguyên roomledger-{stage}-… nên stack này không cần output
 * của stack khác và deploy được độc lập.
 * - Topic SNS roomledger-{stage}-alarms: mọi báo động gửi vào đây (kể cả hai báo động của WorkflowStack). Chưa có người
 *   nhận: Phát chọn email rồi thêm subscription.
 * - Dashboard roomledger-{stage}: API (lỗi, độ trễ), lỗi và throttle của từng Lambda, RDS (CPU, kết nối, dung lượng
 *   trống), Step Functions so ảnh trả phòng, chỉ số RoomLedger/Compare, DLQ.
 * - Ba báo động: API 5xx, Lambda bị throttle (toàn tài khoản), RDS sắp hết dung lượng. Cộng hai báo động của
 *   WorkflowStack là 5, nằm trong free tier (10 báo động) của CloudWatch; một dashboard cũng trong free tier (3).
 *
 * @author Sang
 * @version 1.0
 */
public class ObservabilityStack : Stack
{
    /** Tên ngắn của mọi Lambda (FunctionName = roomledger-{stage}-{tên}): Api, Auth, Data, Workflow. */
    public static readonly string[] FunctionShortNames =
    [
        "api", "post-confirmation", "migrate", "photo-ingest", "photo-derive", "evidence-seal",
        "plan-compare", "compare-pairs", "persist-results", "render-pdf",
    ];

    /** Chỉ số persist-results ghi qua Step Functions (WorkflowStack, namespace RoomLedger/Compare, chiều Stage). */
    private static readonly string[] CompareMetrics =
        ["PairsAnalyzed", "PairsSkipped", "PairsNotAnalyzed", "FallbackUsed", "FindingsSuggested"];

    private static readonly Duration Period = Duration.Minutes(5);

    public ITopic AlarmTopic { get; }

    public ObservabilityStack(Construct scope, string id, ObservabilityStackProps props) : base(scope, id, props)
    {
        var stage = props.Stage;
        string Name(string n) => $"roomledger-{stage}-{n}";
        var apiName = Name("api");
        var dbInstance = $"roomledger-{stage}";
        var stateMachineArn = $"arn:{Partition}:states:{Region}:{Account}:stateMachine:{Name("move-out-compare")}";

        AlarmTopic = new Topic(this, "AlarmTopic", new TopicProps
        {
            TopicName = Name("alarms"),
            DisplayName = "RoomLedger alarms",
        });

        // ---- Dashboard ----
        var api = new Dictionary<string, string> { ["ApiName"] = apiName };
        var db = new Dictionary<string, string> { ["DBInstanceIdentifier"] = dbInstance };
        var dashboard = new Dashboard(this, "Dashboard", new DashboardProps { DashboardName = $"roomledger-{stage}" });
        dashboard.AddWidgets(
            Graph("API: lỗi 4xx / 5xx",
                M("AWS/ApiGateway", "4XXError", api, "Sum"), M("AWS/ApiGateway", "5XXError", api, "Sum")),
            Graph("API: độ trễ (ms)",
                M("AWS/ApiGateway", "Latency", api, "Average"), M("AWS/ApiGateway", "Latency", api, "p90")));
        dashboard.AddWidgets(
            Graph("Lambda: lỗi", FunctionShortNames
                .Select(f => M("AWS/Lambda", "Errors", new() { ["FunctionName"] = Name(f) }, "Sum", f)).ToArray()),
            Graph("Lambda: throttle", FunctionShortNames
                .Select(f => M("AWS/Lambda", "Throttles", new() { ["FunctionName"] = Name(f) }, "Sum", f)).ToArray()));
        dashboard.AddWidgets(
            Graph("RDS: CPU (%) và số kết nối",
                M("AWS/RDS", "CPUUtilization", db, "Average"), M("AWS/RDS", "DatabaseConnections", db, "Maximum")),
            Graph("RDS: dung lượng trống (byte)", M("AWS/RDS", "FreeStorageSpace", db, "Minimum")));
        var sfn = new Dictionary<string, string> { ["StateMachineArn"] = stateMachineArn };
        dashboard.AddWidgets(
            Graph("So ảnh trả phòng: lần chạy",
                M("AWS/States", "ExecutionsSucceeded", sfn, "Sum"), M("AWS/States", "ExecutionsFailed", sfn, "Sum")),
            Graph("So ảnh trả phòng: cặp ảnh và gợi ý", CompareMetrics
                .Select(m => M("RoomLedger/Compare", m, new() { ["Stage"] = stage }, "Sum")).ToArray()),
            Graph("DLQ: sự kiện không giao được",
                M("AWS/SQS", "ApproximateNumberOfMessagesVisible", new() { ["QueueName"] = Name("workflow-dlq") }, "Maximum")));

        // ---- Báo động ----
        Alert("Api5xx", Name("api-5xx"), "API Gateway trả 5XX từ 5 lần trong 5 phút",
            M("AWS/ApiGateway", "5XXError", api, "Sum"), 5, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD);
        Alert("LambdaThrottles", Name("lambda-throttles"),
            "Có Lambda bị throttle (hết concurrency của tài khoản: xin tăng quota hoặc giảm song song)",
            new Metric(new MetricProps { Namespace = "AWS/Lambda", MetricName = "Throttles", Statistic = "Sum", Period = Period }),
            1, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD);
        Alert("RdsFreeStorage", Name("rds-free-storage"), "RDS còn dưới 2 GB dung lượng trống",
            M("AWS/RDS", "FreeStorageSpace", db, "Minimum"), 2d * 1024 * 1024 * 1024, ComparisonOperator.LESS_THAN_THRESHOLD);
    }

    private static Metric M(string ns, string name, Dictionary<string, string> dimensions, string statistic, string? label = null) =>
        new(new MetricProps
        {
            Namespace = ns,
            MetricName = name,
            DimensionsMap = dimensions,
            Statistic = statistic,
            Period = Period,
            Label = label,
        });

    private static GraphWidget Graph(string title, params IMetric[] metrics) =>
        new(new GraphWidgetProps { Title = title, Left = metrics, Width = 8, Height = 6 });

    private void Alert(string id, string name, string description, IMetric metric, double threshold, ComparisonOperator comparison)
    {
        var alarm = new Alarm(this, id, new AlarmProps
        {
            AlarmName = name,
            AlarmDescription = description,
            Metric = metric,
            Threshold = threshold,
            EvaluationPeriods = 1,
            ComparisonOperator = comparison,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        });
        alarm.AddAlarmAction(new SnsAction(AlarmTopic));
    }
}
