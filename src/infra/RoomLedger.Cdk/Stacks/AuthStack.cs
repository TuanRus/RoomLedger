using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.RDS;
using Constructs;

namespace RoomLedger.Cdk.Stacks;

/**
 * Tham số của AuthStack.
 *
 * @author Tuấn
 * @version 1.1
 */
public sealed class AuthStackProps : StackProps
{
    public required string Stage { get; init; }
    public required IVpc Vpc { get; init; }
    public required ISecurityGroup LambdaSg { get; init; }
    public required IDatabaseInstance Db { get; init; }
    /** Gói RoomLedger.Functions (dotnet lambda package), dùng chung với DataStack và WorkflowStack. */
    public required string FunctionsAssetPath { get; init; }
}

/**
 * 2 User Pool: roomledger-{stage}-users (landlord, tenant, cho tự đăng ký) và roomledger-{stage}-admins
 * (admin, tắt tự đăng ký, bắt buộc MFA TOTP). Trigger PostConfirmation gán group và tạo app_user.
 * Mọi thay đổi Cognito và IAM ở đây cần Phát duyệt.
 *
 * @author Tuấn
 * @version 1.1
 */
public sealed class AuthStack : Stack
{
    public UserPool Users { get; }
    public UserPool Admins { get; }
    public UserPoolClient UsersClient { get; }
    public UserPoolClient AdminsClient { get; }

    public AuthStack(Construct scope, string id, AuthStackProps props) : base(scope, id, props)
    {
        string Name(string n) => $"roomledger-{props.Stage}-{n}";
        var removal = props.Stage == "dev" ? RemovalPolicy.DESTROY : RemovalPolicy.RETAIN;

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
            FunctionName = Name("post-confirmation"),
            Runtime = Runtime.DOTNET_8,
            Handler = "RoomLedger.Functions::RoomLedger.Functions.PostConfirmation.Function::HandleAsync",
            Code = Code.FromAsset(props.FunctionsAssetPath),
            MemorySize = 512,
            Timeout = Duration.Seconds(10),
            Vpc = props.Vpc,
            VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
            SecurityGroups = [props.LambdaSg],
            Tracing = Tracing.ACTIVE,
            Environment = new Dictionary<string, string>
            {
                // Không có Password → FunctionDb dùng RDS IAM auth (giống WorkflowStack)
                ["ROOMLEDGER_DB"] = ApiStack.AppConnectionString(props.Db),
            },
            // Không đặt ReservedConcurrentExecutions: giới hạn tài khoản là 10
        });
        props.Db.GrantConnect(postConfirmation, DataStack.AppUser);

        Users = new UserPool(this, "Users", new UserPoolProps
        {
            UserPoolName = Name("users"),
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
            RemovalPolicy = removal,
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
            UserPoolClientName = Name("users-web"),
            GenerateSecret = false,
            AuthFlows = new AuthFlow { UserSrp = true },
            PreventUserExistenceErrors = true,
            IdTokenValidity = Duration.Hours(1),
            AccessTokenValidity = Duration.Hours(1),
            RefreshTokenValidity = Duration.Days(30),
        });

        Admins = new UserPool(this, "Admins", new UserPoolProps
        {
            UserPoolName = Name("admins"),
            SelfSignUpEnabled = false,
            SignInAliases = new SignInAliases { Email = true },
            Mfa = Mfa.REQUIRED,
            MfaSecondFactor = new MfaSecondFactor { Otp = true, Sms = false },
            PasswordPolicy = passwordPolicy,
            AccountRecovery = AccountRecovery.EMAIL_ONLY,
            RemovalPolicy = removal,
        });
        _ = new CfnUserPoolGroup(this, "AdminGroup", new CfnUserPoolGroupProps { UserPoolId = Admins.UserPoolId, GroupName = "admin" });
        AdminsClient = Admins.AddClient("Web", new UserPoolClientOptions
        {
            UserPoolClientName = Name("admins-web"),
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
