using Amazon.CDK;
using RoomLedger.Cdk.Stacks;

var app = new App();
var stage = (string?)app.Node.TryGetContext("stage") ?? "dev";   // dev | demo
// SCP của AWS Organization chỉ cho ap-southeast-2 (ap-southeast-1 và us-east-1 bị chặn, kiểm tra 2026-11-05).
var region = (string?)app.Node.TryGetContext("region") ?? "ap-southeast-2";
var env = new Amazon.CDK.Environment { Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"), Region = region };
StackProps P() => new() { Env = env };
string Id(string name) => $"roomledger-{stage}-{name}";

// Lambda đã đóng gói. Chạy trước (từ thư mục gốc repo):
//   dotnet lambda package --project-location src/backend/src/RoomLedger.Functions -c Release -o artifacts/functions.zip
// Hoặc truyền -c functionsAsset=<zip hoặc thư mục>.
var functionsAsset = (string?)app.Node.TryGetContext("functionsAsset")
    ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "artifacts", "functions.zip"));
if (!File.Exists(functionsAsset) && !Directory.Exists(functionsAsset))
{
    throw new InvalidOperationException(
        $"Không tìm thấy gói Lambda tại '{functionsAsset}'. Từ thư mục gốc repo chạy: dotnet lambda package " +
        "--project-location src/backend/src/RoomLedger.Functions -c Release -o artifacts/functions.zip " +
        "(hoặc truyền -c functionsAsset=<đường dẫn>).");
}

var network = new NetworkStack(app, Id("network"), new NetworkStackProps { Env = env, Stage = stage });
new SecurityStack(app, Id("security"), P());
var data = new DataStack(app, Id("data"), new DataStackProps
{
    Env = env,
    Stage = stage,
    Vpc = network.Vpc,
    LambdaSg = network.LambdaSg,
    FunctionsAssetPath = functionsAsset,
});
new StorageStack(app, Id("storage"), P());
new AuthStack(app, Id("auth"), P());
new ApiStack(app, Id("api"), P());
// Tên bucket theo quy ước roomledger-{stage}-{tên}; StorageStack (Phát) phải dùng đúng tên này và bật
// EventBridge notifications cho bucket incoming. Ghi đè bằng -c incomingBucket=... -c derivedBucket=...
new WorkflowStack(app, Id("workflow"), new WorkflowStackProps
{
    Env = env,
    Stage = stage,
    Vpc = network.Vpc,
    LambdaSg = network.LambdaSg,
    Db = data.Db,
    FunctionsAssetPath = functionsAsset,
    IncomingBucketName = (string?)app.Node.TryGetContext("incomingBucket") ?? $"roomledger-{stage}-incoming",
    DerivedBucketName = (string?)app.Node.TryGetContext("derivedBucket") ?? $"roomledger-{stage}-derived",
    EvidenceBucketName = (string?)app.Node.TryGetContext("evidenceBucket") ?? $"roomledger-{stage}-evidence",
});
// WAF cho CloudFront phải ở us-east-1, nhưng SCP chặn us-east-1: synth được, deploy sẽ lỗi. Cần cả nhóm quyết định.
new EdgeStack(app, Id("edge"), new StackProps { Env = new Amazon.CDK.Environment { Region = "us-east-1" } });
new ObservabilityStack(app, Id("observability"), P());

Tags.Of(app).Add("Project", "RoomLedger");
Tags.Of(app).Add("Stage", stage);

app.Synth();
