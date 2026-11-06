using Amazon.CDK;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/// <summary>EventBridge, Step Functions, Lambda xử lý ảnh và AI, persist-results, notify, SES, SSM. Phụ trách: Sang.</summary>
public class WorkflowStack : Stack
{
    public WorkflowStack(Construct scope, string id, IStackProps? props = null) : base(scope, id, props)
    {
    }
}
