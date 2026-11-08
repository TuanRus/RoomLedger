using Amazon.CDK;
using RoomLedger.Cdk.Stacks;

var app = new App();
var stage = (string?)app.Node.TryGetContext("stage") ?? "dev";   // dev | demo
var account = (string?)app.Node.TryGetContext("account") ?? System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT") ?? "123456789012";
var region = (string?)app.Node.TryGetContext("region") ?? "ap-southeast-1";
var cfg = new StageConfig(stage, account, region);
var env = new Amazon.CDK.Environment { Account = account, Region = region };

// Thư mục chứa bản dotnet publish của Lambda (CI publish trước khi synth/deploy)
var apiCode = (string?)app.Node.TryGetContext("apiCodePath") ?? "../../backend/src/RoomLedger.Api/bin/Release/net8.0/publish";
var functionsCode = (string?)app.Node.TryGetContext("functionsCodePath") ?? "../../backend/src/RoomLedger.Functions/bin/Release/net8.0/publish";

StackProps P(string owner) => new() { Env = env, Tags = new Dictionary<string, string> { ["Owner"] = owner } };

var network = new NetworkStack(app, cfg.StackName("Network"), cfg, P("Phat"));
_ = new SecurityStack(app, cfg.StackName("Security"), P("Phat"));
var data = new DataStack(app, cfg.StackName("Data"), cfg, network, functionsCode, P("Tuan"));
var storage = new StorageStack(app, cfg.StackName("Storage"), cfg, P("Phat"));
var auth = new AuthStack(app, cfg.StackName("Auth"), cfg, network, data, functionsCode, P("Tuan"));
_ = new ApiStack(app, cfg.StackName("Api"), cfg, network, storage, data, auth, apiCode, P("Tuan"));
_ = new WorkflowStack(app, cfg.StackName("Workflow"), P("Sang"));
// WAF cho CloudFront phải ở us-east-1
_ = new EdgeStack(app, cfg.StackName("Edge"), new StackProps
{
    Env = new Amazon.CDK.Environment { Account = account, Region = "us-east-1" },
    Tags = new Dictionary<string, string> { ["Owner"] = "Kiu" },
});
_ = new ObservabilityStack(app, cfg.StackName("Observability"), P("Sang"));

Tags.Of(app).Add("Project", "RoomLedger");
Tags.Of(app).Add("Stage", stage);

app.Synth();
