using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>RDS PostgreSQL, Lambda migrate. Phụ trách: Tuấn.</summary>
public class DataStack : Stack
{
    public DataStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
