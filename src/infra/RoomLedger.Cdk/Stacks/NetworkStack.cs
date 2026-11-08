using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * VPC 10.0.0.0/16, 2 private subnet (không NAT), Gateway endpoint S3, 5 Interface endpoint, security group, Flow Logs.
 * Bản nền để các stack khác synth được; Phát rà soát và hoàn thiện (mọi thay đổi VPC, security group cần Phát duyệt).
 *
 * @author Tuấn
 * @version 1.0
 */
public class NetworkStack : Stack
{
    public Vpc Vpc { get; }
    public SecurityGroup LambdaSecurityGroup { get; }
    public SecurityGroup RdsSecurityGroup { get; }

    public NetworkStack(Construct scope, string id, StageConfig cfg, IStackProps? props = null) : base(scope, id, props)
    {
        Vpc = new Vpc(this, "Vpc", new VpcProps
        {
            VpcName = cfg.Name("vpc"),
            IpAddresses = IpAddresses.Cidr("10.0.0.0/16"),
            // Ghi rõ AZ để synth không cần gọi AWS tra cứu (CI chưa có credentials)
            AvailabilityZones = [$"{cfg.Region}a", $"{cfg.Region}b"],
            NatGateways = 0,
            SubnetConfiguration =
            [
                new SubnetConfiguration { Name = "private", SubnetType = SubnetType.PRIVATE_ISOLATED, CidrMask = 24 },
            ],
            FlowLogs = new Dictionary<string, IFlowLogOptions>
            {
                ["reject"] = new FlowLogOptions { TrafficType = FlowLogTrafficType.REJECT },
            },
        });

        LambdaSecurityGroup = new SecurityGroup(this, "LambdaSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = cfg.Name("lambda-sg"),
            Description = "Lambda trong VPC",
            AllowAllOutbound = true,
        });
        RdsSecurityGroup = new SecurityGroup(this, "RdsSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = cfg.Name("rds-sg"),
            Description = "RDS chi nhan 5432 tu lambda-sg",
            AllowAllOutbound = false,
        });
        RdsSecurityGroup.AddIngressRule(LambdaSecurityGroup, Port.Tcp(5432), "PostgreSQL tu Lambda");

        Vpc.AddGatewayEndpoint("S3", new GatewayVpcEndpointOptions { Service = GatewayVpcEndpointAwsService.S3 });
        var endpointSg = new SecurityGroup(this, "EndpointSg", new SecurityGroupProps
        {
            Vpc = Vpc,
            SecurityGroupName = cfg.Name("endpoint-sg"),
            Description = "Interface endpoint chi nhan 443 tu lambda-sg",
            AllowAllOutbound = false,
        });
        endpointSg.AddIngressRule(LambdaSecurityGroup, Port.Tcp(443), "HTTPS tu Lambda");
        var services = new Dictionary<string, InterfaceVpcEndpointAwsService>
        {
            ["EventBridge"] = InterfaceVpcEndpointAwsService.EVENTBRIDGE,
            ["Ssm"] = InterfaceVpcEndpointAwsService.SSM,
            ["XRay"] = InterfaceVpcEndpointAwsService.XRAY,
            ["SecretsManager"] = InterfaceVpcEndpointAwsService.SECRETS_MANAGER,
            ["Cognito"] = new InterfaceVpcEndpointAwsService("cognito-idp"),
        };
        foreach (var (name, service) in services)
        {
            Vpc.AddInterfaceEndpoint(name, new InterfaceVpcEndpointOptions
            {
                Service = service,
                PrivateDnsEnabled = true,
                SecurityGroups = [endpointSg],
            });
        }
    }
}
