using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>S3 web, CloudFront, WAF CloudFront, WAF Regional. Phụ trách: Kỉu, Phát.</summary>
public class EdgeStack : Stack
{
    public EdgeStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
