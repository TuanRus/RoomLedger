using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>3 bucket incoming / evidence (Object Lock) / derived, lifecycle, bucket policy. Phụ trách: Phát.</summary>
public class StorageStack : Stack
{
    public StorageStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
