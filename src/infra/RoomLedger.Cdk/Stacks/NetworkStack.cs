using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>VPC 10.0.0.0/16, 2 private subnet, Gateway endpoint S3, 5 Interface endpoint, security group, Flow Logs. Phụ trách: Phát.</summary>
public class NetworkStack : Stack
{
    public NetworkStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
