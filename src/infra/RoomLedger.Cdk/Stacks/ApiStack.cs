using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>API Gateway REST, Cognito authorizer, Lambda api, photo-ingest, evidence-seal. Phụ trách: Tuấn.</summary>
public class ApiStack : Stack
{
    public ApiStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
