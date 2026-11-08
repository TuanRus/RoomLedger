using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * 2 User Pool: roomledger-{stage}-users (landlord, tenant, cho tự đăng ký) và roomledger-{stage}-admins
 * (admin, tắt tự đăng ký, bắt buộc MFA TOTP). Trigger PostConfirmation gán group và tạo app_user.
 * Mọi thay đổi Cognito và IAM ở đây cần Phát duyệt.
 *
 * @author Tuấn
 * @version 1.0
 */
public class AuthStack : Stack
{
    public UserPool Users { get; }
    public UserPool Admins { get; }
    public UserPoolClient UsersClient { get; }
    public UserPoolClient AdminsClient { get; }

    public AuthStack(Construct scope, string id, StageConfig cfg, NetworkStack network, DataStack data, string functionsCodePath, IStackProps? props = null)
        : base(scope, id, props)
    {
        var passwordPolicy = new PasswordPolicy
        {
            MinLength = 8,
            RequireLowercase = true,
            RequireUppercase = true,
            RequireDigits = true,
            RequireSymbols = true,
        };

        var postConfirmation = new Function(this, "PostConfirmation", new FunctionProps
        {
            FunctionName = cfg.Name("post-confirmation"),
            Runtime = Runtime.DOTNET_8,
            Architecture = Architecture.ARM_64,
            Handler = "RoomLedger.Functions::RoomLedger.Functions.PostConfirmation.Function::HandleAsync",
            Code = Code.FromAsset(functionsCodePath),
            MemorySize = 512,
            Timeout = Duration.Seconds(10),
            Vpc = network.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = [network.LambdaSecurityGroup],
            Tracing = Tracing.ACTIVE,
            Environment = new Dictionary<string, string>
            {
                ["ROOMLEDGER_DB"] = ApiStack.AppConnectionString(data),
            },
        });
        ApiStack.GrantDbConnect(postConfirmation, data, this);

        Users = new UserPool(this, "Users", new UserPoolProps
        {
            UserPoolName = cfg.Name("users"),
            SelfSignUpEnabled = true,
            SignInAliases = new SignInAliases { Email = true },
            AutoVerify = new AutoVerifiedAttrs { Email = true },
            StandardAttributes = new StandardAttributes
            {
                Email = new StandardAttribute { Required = true, Mutable = true },
                Fullname = new StandardAttribute { Required = true, Mutable = true },
            },
            PasswordPolicy = passwordPolicy,
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            LambdaTriggers = new UserPoolTriggers { PostConfirmation = postConfirmation },
            RemovalPolicy = cfg.Removal,
        });
        _ = new CfnUserPoolGroup(this, "LandlordGroup", new CfnUserPoolGroupProps { UserPoolId = Users.UserPoolId, GroupName = "landlord" });
        _ = new CfnUserPoolGroup(this, "TenantGroup", new CfnUserPoolGroupProps { UserPoolId = Users.UserPoolId, GroupName = "tenant" });

        // Policy riêng (không qua AddToRolePolicy) để tránh vòng phụ thuộc pool ↔ trigger
        new Policy(this, "PostConfirmationGroups", new PolicyProps
        {
            Statements =
            [
                new PolicyStatement(new PolicyStatementProps
                {
                    Actions = ["cognito-idp:AdminAddUserToGroup"],
                    Resources = [Users.UserPoolArn],
                }),
            ],
        }).AttachToRole(postConfirmation.Role!);

        UsersClient = Users.AddClient("Web", new UserPoolClientOptions
        {
            UserPoolClientName = cfg.Name("users-web"),
            GenerateSecret = false,
            AuthFlows = new AuthFlow { UserSrp = true },
            PreventUserExistenceErrors = true,
            IdTokenValidity = Duration.Hours(1),
            AccessTokenValidity = Duration.Hours(1),
            RefreshTokenValidity = Duration.Days(30),
        });

        Admins = new UserPool(this, "Admins", new UserPoolProps
        {
            UserPoolName = cfg.Name("admins"),
            SelfSignUpEnabled = false,
            SignInAliases = new SignInAliases { Email = true },
            Mfa = Mfa.REQUIRED,
            MfaSecondFactor = new MfaSecondFactor { Otp = true, Sms = false },
            PasswordPolicy = passwordPolicy,
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            RemovalPolicy = cfg.Removal,
        });
        _ = new CfnUserPoolGroup(this, "AdminGroup", new CfnUserPoolGroupProps { UserPoolId = Admins.UserPoolId, GroupName = "admin" });
        AdminsClient = Admins.AddClient("Web", new UserPoolClientOptions
        {
            UserPoolClientName = cfg.Name("admins-web"),
            GenerateSecret = false,
            AuthFlows = new AuthFlow { UserSrp = true },
            PreventUserExistenceErrors = true,
            IdTokenValidity = Duration.Hours(1),
            AccessTokenValidity = Duration.Hours(1),
            RefreshTokenValidity = Duration.Days(1),
        });

        _ = new CfnOutput(this, "UsersPoolId", new CfnOutputProps { Value = Users.UserPoolId });
        _ = new CfnOutput(this, "UsersClientId", new CfnOutputProps { Value = UsersClient.UserPoolClientId });
        _ = new CfnOutput(this, "AdminsPoolId", new CfnOutputProps { Value = Admins.UserPoolId });
        _ = new CfnOutput(this, "AdminsClientId", new CfnOutputProps { Value = AdminsClient.UserPoolClientId });
    }
}
