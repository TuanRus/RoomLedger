using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>2 User Pool: landlord·tenant và admin (MFA). Phụ trách: Tuấn.</summary>
public class AuthStack : Stack
{
    public AuthStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
