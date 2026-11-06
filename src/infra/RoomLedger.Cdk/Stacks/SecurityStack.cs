using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>KMS, CloudTrail, GuardDuty, Budgets, Secrets Manager, OIDC role cho GitHub Actions. Phụ trách: Phát.</summary>
public class SecurityStack : Stack
{
    public SecurityStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
