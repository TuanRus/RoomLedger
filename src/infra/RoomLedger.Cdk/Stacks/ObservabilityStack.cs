using Amazon.CDK;
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
 * Dashboard, báo động, topic SNS nhận báo động. Phụ trách: Sang.
 *
 * @author Sang
 * @version 1.0
 */
public class ObservabilityStack : Stack
{
    public static readonly string[] FunctionShortNames = [];

    public ITopic AlarmTopic { get; } = null!;

    public ObservabilityStack(Construct scope, string id, ObservabilityStackProps props) : base(scope, id, props)
    {
    }
}
