using Amazon.CDK;
using Amazon.CDK.Assertions;
using Xunit;

using RoomLedger.Cdk.Stacks;

namespace RoomLedger.Cdk.Tests;

/// <summary>T5 — Network stack.</summary>
public class NetworkStackTests
{
    private const string Region = "ap-southeast-2";

    // Synth is slow (jsii); build the dev template once and share it (templates are read-only).
    private static readonly Lazy<Template> DevTemplate = new(() => Build("dev"));

    private static readonly Lazy<Template> ProdTemplate = new(() => Build("prod"));

    private static Template Synth(string stage = "dev") => stage == "dev" ? DevTemplate.Value : Build(stage);

    private static Template Build(string stage)
    {
        var app = new App();
        var stack = new NetworkStack(app, "TestNetwork", new NetworkStackProps
        {
            Stage = stage,
            Env = new Amazon.CDK.Environment { Account = "123456789012", Region = Region },
        });
        return Template.FromStack(stack);
    }

    private static IEnumerable<IDictionary<string, object>> PropsOf(Template t, string type) =>
        t.FindResources(type).Values.Select(r => (IDictionary<string, object>)r["Properties"]);

    [Fact]
    public void Creates_exactly_one_vpc()
        => Synth().ResourceCountIs("AWS::EC2::VPC", 1);

    [Fact]
    public void Has_no_nat_gateway_or_internet_gateway()
    {
        var t = Synth();
        t.ResourceCountIs("AWS::EC2::NatGateway", 0);
        t.ResourceCountIs("AWS::EC2::InternetGateway", 0);
        t.ResourceCountIs("AWS::EC2::EIP", 0);
    }

    [Fact]
    public void Has_two_private_subnets_in_two_azs_without_public_ips()
    {
        var subnets = PropsOf(Synth(), "AWS::EC2::Subnet").ToList();
        Assert.Equal(2, subnets.Count);
        Assert.Equal(new[] { "ap-southeast-2a", "ap-southeast-2b" },
                     subnets.Select(s => (string)s["AvailabilityZone"]).OrderBy(z => z));
        Assert.All(subnets, s => Assert.False((bool)s["MapPublicIpOnLaunch"]));
    }

    [Fact]
    public void Has_s3_gateway_endpoint()
        => Synth().HasResourceProperties("AWS::EC2::VPCEndpoint", new Dictionary<string, object>
        {
            ["VpcEndpointType"] = "Gateway",
        });

    [Theory]
    [InlineData("dev", 1)]   // 1 AZ để tiết kiệm (D10)
    [InlineData("prod", 2)]  // sơ đồ final: mỗi AZ một endpoint Cognito
    public void Has_only_the_cognito_interface_endpoint_with_private_dns(string stage, int subnets)
    {
        var interfaces = PropsOf(Synth(stage), "AWS::EC2::VPCEndpoint")
            .Where(p => p["VpcEndpointType"] as string == "Interface")
            .ToList();

        var ep = Assert.Single(interfaces);
        Assert.Equal($"com.amazonaws.{Region}.cognito-idp", ep["ServiceName"]);
        Assert.True((bool)ep["PrivateDnsEnabled"]);
        Assert.Equal(subnets, ((object[])ep["SubnetIds"]).Length);
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("prod")]
    public void Has_exactly_two_endpoints(string stage)
        => Synth(stage).ResourceCountIs("AWS::EC2::VPCEndpoint", 2);

    [Fact]
    public void Has_lambda_security_group_named_by_stage()
        => Synth("dev").HasResourceProperties("AWS::EC2::SecurityGroup", new Dictionary<string, object>
        {
            ["GroupName"] = "roomledger-dev-lambda-sg",
        });

    [Fact]
    public void No_security_group_has_inline_cidr_ingress()
    {
        // Interface endpoints default to "Open" = 443 from the whole VPC CIDR, written inline.
        var inline = PropsOf(Synth(), "AWS::EC2::SecurityGroup")
            .Where(p => p.ContainsKey("SecurityGroupIngress"))
            .SelectMany(p => (object[])p["SecurityGroupIngress"])
            .Cast<IDictionary<string, object>>()
            .Where(rule => rule.ContainsKey("CidrIp") || rule.ContainsKey("CidrIpv6"))
            .ToList();
        Assert.Empty(inline);
    }

    [Fact]
    public void Endpoints_accept_https_from_lambda_sg_only()
    {
        var t = Synth();
        var lambdaSgId = t.FindResources("AWS::EC2::SecurityGroup", new Dictionary<string, object>
        {
            ["Properties"] = new Dictionary<string, object> { ["GroupName"] = "roomledger-dev-lambda-sg" },
        }).Keys.Single();

        var ingress = PropsOf(t, "AWS::EC2::SecurityGroupIngress").ToList();
        Assert.NotEmpty(ingress);
        Assert.All(ingress, rule =>
        {
            Assert.Equal(443, Convert.ToInt32(rule["FromPort"]));
            Assert.Equal(443, Convert.ToInt32(rule["ToPort"]));
            Assert.Contains(lambdaSgId, System.Text.Json.JsonSerializer.Serialize(rule["SourceSecurityGroupId"]));
        });
    }
}
