using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>Dashboard, alarm, X-Ray, log retention. Phụ trách: Sang.</summary>
public class ObservabilityStack : Stack
{
    public ObservabilityStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
