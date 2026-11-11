using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của NetworkStack.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class NetworkStackProps : StackProps
{
    public required string Stage { get; init; }
}

/**
 * VPC 10.0.0.0/16 chỉ có 2 private subnet (không NAT, không Internet Gateway).
 * Lambda gọi dịch vụ AWS qua VPC endpoint: S3 (Gateway) + SSM, EventBridge, Secrets Manager, X-Ray, Cognito
 * (Interface, 1 AZ để tiết kiệm). Chỉ lambda-sg được vào endpoint.
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
            SecurityGroupName = $"roomledger-{props.Stage}-lambda-sg",
            Description = "RoomLedger Lambdas inside the VPC",
            AllowAllOutbound = true,
        });

        Vpc.AddGatewayEndpoint("S3", new GatewayVpcEndpointOptions { Service = GatewayVpcEndpointAwsService.S3 });

        var oneAz = new SubnetSelection
        {
            SubnetType = SubnetType.PRIVATE_ISOLATED,
            AvailabilityZones = new[] { Vpc.AvailabilityZones[0] },
        };

        var endpoints = new (string Id, IInterfaceVpcEndpointService Service)[]
        {
            ("Ssm", InterfaceVpcEndpointAwsService.SSM),
            ("Events", InterfaceVpcEndpointAwsService.EVENTBRIDGE),
            ("Secrets", InterfaceVpcEndpointAwsService.SECRETS_MANAGER),
            ("Xray", InterfaceVpcEndpointAwsService.XRAY),
            ("Cognito", new InterfaceVpcEndpointAwsService("cognito-idp")),
        };

        foreach (var (epId, service) in endpoints)
        {
            var endpoint = Vpc.AddInterfaceEndpoint(epId, new InterfaceVpcEndpointOptions
            {
                Service = service,
                Subnets = oneAz,
                PrivateDnsEnabled = true,
                Open = false, // chỉ lambda-sg, không mở cho cả dải CIDR của VPC
            });
            endpoint.Connections.AllowDefaultPortFrom(LambdaSg, "HTTPS from RoomLedger Lambdas");
        }

        new CfnOutput(this, "VpcId", new CfnOutputProps { Value = Vpc.VpcId });
        new CfnOutput(this, "LambdaSgId", new CfnOutputProps { Value = LambdaSg.SecurityGroupId });
    }
}
