using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của NetworkStack.
 *
 * @author Sang
 * @version 1.1
 */
public sealed class NetworkStackProps : StackProps
{
    public required string Stage { get; init; }
}

/**
 * VPC 10.0.0.0/16 chỉ có 2 private subnet (không NAT, không Internet Gateway).
 * Lambda gọi dịch vụ AWS qua VPC endpoint (sơ đồ final): S3 (Gateway) và một Interface endpoint cho Cognito
 * (post-confirmation). Chỉ lambda-sg được vào endpoint. dev/demo đặt ở 1 AZ để tiết kiệm, prod đặt ở cả 2 AZ.
 * Sự kiện đi qua bucket outbox và mọi Lambda đăng nhập RDS bằng IAM, nên không có endpoint EventBridge hay Secrets Manager.
 * Phụ trách: Phát (Sang viết bản đầu, T5). Chưa có Flow Logs.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class NetworkStack : Stack
{
    public Vpc Vpc { get; }
    public SecurityGroup LambdaSg { get; }

    public NetworkStack(Construct scope, string id, NetworkStackProps props) : base(scope, id, props)
    {
        Vpc = new Vpc(this, "Vpc", new VpcProps
        {
            // AZ ghi cứng: synth không cần tra context (region cố định ap-southeast-2).
            AvailabilityZones = new[] { $"{Region}a", $"{Region}b" },
            NatGateways = 0,
            SubnetConfiguration = new ISubnetConfiguration[]
            {
                new SubnetConfiguration { Name = "private", SubnetType = SubnetType.PRIVATE_ISOLATED, CidrMask = 24 },
            },
        });

        LambdaSg = new SecurityGroup(this, "LambdaSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = Naming.Resource(props.Stage, "lambda-sg"),
            Description = "RoomLedger Lambdas inside the VPC",
            AllowAllOutbound = true,
        });

        Vpc.AddGatewayEndpoint("S3", new GatewayVpcEndpointOptions { Service = GatewayVpcEndpointAwsService.S3 });

        var cognito = Vpc.AddInterfaceEndpoint("Cognito", new InterfaceVpcEndpointOptions
        {
            Service = new InterfaceVpcEndpointAwsService("cognito-idp"),
            Subnets = new SubnetSelection
            {
                SubnetType = SubnetType.PRIVATE_ISOLATED,
                AvailabilityZones = props.Stage == "prod" ? Vpc.AvailabilityZones : new[] { Vpc.AvailabilityZones[0] },
            },
            PrivateDnsEnabled = true,
            Open = false, // chỉ lambda-sg, không mở cho cả dải CIDR của VPC
        });
        cognito.Connections.AllowDefaultPortFrom(LambdaSg, "HTTPS from RoomLedger Lambdas");

        new CfnOutput(this, "VpcId", new CfnOutputProps { Value = Vpc.VpcId });
        new CfnOutput(this, "LambdaSgId", new CfnOutputProps { Value = LambdaSg.SecurityGroupId });
    }
}
