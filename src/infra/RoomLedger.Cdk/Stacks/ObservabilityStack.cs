using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.SNS;
using Constructs;
using RoomLedger.Cdk;

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
 * Giám sát RoomLedger (Sang). Mọi chỉ số lấy theo tên tài nguyên (Naming.Resource) nên stack này không cần output
 * của stack khác và deploy được độc lập.
 * - Topic SNS roomledger-{stage}-alarms: mọi báo động gửi vào đây (kể cả báo động DLQ của WorkflowStack). Chưa có người
 *   nhận: Phát chọn email rồi thêm subscription.
 * - Dashboard roomledger-{stage}: API (lỗi, độ trễ), lỗi và throttle của từng Lambda, RDS (CPU, kết nối, dung lượng
 *   trống), DLQ.
 * - 14 báo động ở đây: API 5xx và độ trễ, Lambda bị throttle (toàn tài khoản), lỗi của 6 Lambda, tuổi tin nhắn cũ nhất của
 *   photo-queue, RDS (hết dung lượng, CPU, số kết nối, thiếu bộ nhớ). Cộng báo động DLQ của WorkflowStack là 15
 *   (vượt 10 báo động free tier, tính phí theo Pricing Calculator); một dashboard nằm trong free tier (3).
 *
 * @author Sang
 * @version 1.3
 */
public class ObservabilityStack : Stack
{
    /** Tên ngắn của mọi Lambda (FunctionName = Naming.Resource(stage, tên)): Api, Auth, Data, Workflow. */
    public static readonly string[] FunctionShortNames =
    [
        "api", "post-confirmation", "migrate", "photo-ingest", "photo-derive", "evidence-seal",
        "render-pdf",
    ];

    /** Lambda có báo động lỗi riêng: 6 trong 7 Lambda của FunctionShortNames, trừ migrate. */
    public static readonly string[] AlarmedLambdas =
        ["api", "post-confirmation", "photo-ingest", "photo-derive", "evidence-seal", "render-pdf"];

    /** Số báo động của stack này (cộng 1 báo động DLQ của WorkflowStack là 15). */
    public const int AlarmCount = 14;

    private static readonly Duration Period = Duration.Minutes(5);

    public ITopic AlarmTopic { get; }

    public ObservabilityStack(Construct scope, string id, ObservabilityStackProps props) : base(scope, id, props)
    {
        var stage = props.Stage;
        string Name(string n) => Naming.Resource(stage, n);
        var apiName = Name("api");
        var dbInstance = $"{Naming.Project}-{stage}";   // = DataStack InstanceIdentifier

        AlarmTopic = new Topic(this, "AlarmTopic", new TopicProps
        {
            TopicName = Name("alarms"),
            DisplayName = "RoomLedger alarms",
        });

        // ---- Dashboard ----
        var api = new Dictionary<string, string> { ["ApiName"] = apiName };
        var db = new Dictionary<string, string> { ["DBInstanceIdentifier"] = dbInstance };
        var dashboard = new Dashboard(this, "Dashboard", new DashboardProps { DashboardName = $"{Naming.Project}-{stage}" });
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
        dashboard.AddWidgets(
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

        Alert("ApiLatencyP90", Name("api-latency-p90"), "Độ trễ p90 của API từ 5 giây trong 5 phút",
            M("AWS/ApiGateway", "Latency", api, "p90"), 5000, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD);

        // Lỗi từng Lambda (migrate chạy tay khi deploy nên không có báo động)
        foreach (var fn in AlarmedLambdas)
        {
            Alert($"LambdaErrors{string.Concat(fn.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]))}", Name($"{fn}-errors"),
                $"Lambda {Name(fn)} báo lỗi từ 1 lần trong 5 phút",
                M("AWS/Lambda", "Errors", new() { ["FunctionName"] = Name(fn) }, "Sum"), 1, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD);
        }

        Alert("PhotoQueueAge", Name("photo-queue-age"), "Tin nhắn cũ nhất trong photo-queue chờ quá 5 phút (photo-derive chậm hoặc kẹt)",
            M("AWS/SQS", "ApproximateAgeOfOldestMessage", new() { ["QueueName"] = Name("photo-queue") }, "Maximum"),
            300, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD);

        Alert("RdsCpu", Name("rds-cpu"), "CPU của RDS từ 80% trong 5 phút",
            M("AWS/RDS", "CPUUtilization", db, "Average"), 80, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD);
        Alert("RdsConnections", Name("rds-connections"), "RDS có từ 60 kết nối cùng lúc (Lambda mở quá nhiều kết nối)",
            M("AWS/RDS", "DatabaseConnections", db, "Maximum"), 60, ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD);
        Alert("RdsFreeableMemory", Name("rds-freeable-memory"), "RDS còn dưới 128 MB bộ nhớ trống",
            M("AWS/RDS", "FreeableMemory", db, "Minimum"), 128d * 1024 * 1024, ComparisonOperator.LESS_THAN_THRESHOLD);
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
