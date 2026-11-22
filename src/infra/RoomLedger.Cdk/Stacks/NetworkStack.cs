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
 * Lambda gọi dịch vụ AWS qua VPC endpoint: S3 (Gateway) + Interface endpoint. Chỉ lambda-sg được vào endpoint.
 * - dev/demo: SSM, EventBridge, Secrets Manager, X-Ray, Cognito, 1 AZ để tiết kiệm.
 * - prod (sơ đồ v8.5.2): EventBridge ở cả 2 AZ. Secrets Manager (migrate) và Cognito (post-confirmation) tạm giữ 1 AZ
 *   cho tới khi migrate dùng IAM auth và post-confirmation ra khỏi VPC; SSM (không dùng) và X-Ray bỏ.
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

        var oneAz = new SubnetSelection
        {
            SubnetType = SubnetType.PRIVATE_ISOLATED,
            AvailabilityZones = new[] { Vpc.AvailabilityZones[0] },
        };
        var bothAz = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED };

        var endpoints = props.Stage == "prod"
            ? new (string Id, IInterfaceVpcEndpointService Service, SubnetSelection Subnets)[]
            {
                ("Events", InterfaceVpcEndpointAwsService.EVENTBRIDGE, bothAz),
                ("Secrets", InterfaceVpcEndpointAwsService.SECRETS_MANAGER, oneAz),
                ("Cognito", new InterfaceVpcEndpointAwsService("cognito-idp"), oneAz),
            }
            : new (string Id, IInterfaceVpcEndpointService Service, SubnetSelection Subnets)[]
            {
                ("Ssm", InterfaceVpcEndpointAwsService.SSM, oneAz),
                ("Events", InterfaceVpcEndpointAwsService.EVENTBRIDGE, oneAz),
                ("Secrets", InterfaceVpcEndpointAwsService.SECRETS_MANAGER, oneAz),
                ("Xray", InterfaceVpcEndpointAwsService.XRAY, oneAz),
                ("Cognito", new InterfaceVpcEndpointAwsService("cognito-idp"), oneAz),
            };

        foreach (var (epId, service, subnets) in endpoints)
        {
            var endpoint = Vpc.AddInterfaceEndpoint(epId, new InterfaceVpcEndpointOptions
            {
                Service = service,
                Subnets = subnets,
                PrivateDnsEnabled = true,
                Open = false, // chỉ lambda-sg, không mở cho cả dải CIDR của VPC
            });
            endpoint.Connections.AllowDefaultPortFrom(LambdaSg, "HTTPS from RoomLedger Lambdas");
        }

        new CfnOutput(this, "VpcId", new CfnOutputProps { Value = Vpc.VpcId });
        new CfnOutput(this, "LambdaSgId", new CfnOutputProps { Value = LambdaSg.SecurityGroupId });
    }
}
