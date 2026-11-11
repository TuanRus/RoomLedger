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

// Gói Lambda api (ASP.NET Core). Chạy trước (từ thư mục gốc repo):
//   dotnet lambda package --project-location src/backend/src/RoomLedger.Api -c Release -o artifacts/api.zip
// Hoặc truyền -c apiAsset=<zip hoặc thư mục>.
var apiAsset = (string?)app.Node.TryGetContext("apiAsset")
    ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "artifacts", "api.zip"));
if (!File.Exists(apiAsset) && !Directory.Exists(apiAsset))
{
    throw new InvalidOperationException(
        $"Không tìm thấy gói Lambda api tại '{apiAsset}'. Từ thư mục gốc repo chạy: dotnet lambda package " +
        "--project-location src/backend/src/RoomLedger.Api -c Release -o artifacts/api.zip " +
        "(hoặc truyền -c apiAsset=<đường dẫn>).");
}

// Tên bucket roomledger-{stage}-{tên}-{account}, dùng chung cho StorageStack và WorkflowStack.
// Tên S3 là duy nhất toàn cầu nên thêm hậu tố account (Aws.ACCOUNT_ID tự thay khi deploy, không cần biết trước).
// Ghi đè bằng -c incomingBucket=... -c derivedBucket=... -c evidenceBucket=...
string BucketName(string purpose) =>
    (string?)app.Node.TryGetContext($"{purpose}Bucket") ?? $"roomledger-{stage}-{purpose}-{Aws.ACCOUNT_ID}";
var incomingBucket = BucketName("incoming");
var derivedBucket = BucketName("derived");
var evidenceBucket = BucketName("evidence");

var network = new NetworkStack(app, Id("network"), new NetworkStackProps { Env = env, Stage = stage });
new SecurityStack(app, Id("security"), new SecurityStackProps { Env = env, Stage = stage });
var data = new DataStack(app, Id("data"), new DataStackProps
{
    Env = env,
    Stage = stage,
    Vpc = network.Vpc,
    LambdaSg = network.LambdaSg,
    FunctionsAssetPath = functionsAsset,
});
var storage = new StorageStack(app, Id("storage"), new StorageStackProps
{
    Env = env,
    Stage = stage,
    IncomingBucketName = incomingBucket,
    DerivedBucketName = derivedBucket,
    EvidenceBucketName = evidenceBucket,
    WebOrigin = (string?)app.Node.TryGetContext("webOrigin"),   // bắt buộc khi stage khác dev, xem StorageStack
});
var auth = new AuthStack(app, Id("auth"), new AuthStackProps
{
    Env = env,
    Stage = stage,
    Vpc = network.Vpc,
    LambdaSg = network.LambdaSg,
    Db = data.Db,
    FunctionsAssetPath = functionsAsset,
});
new ApiStack(app, Id("api"), new ApiStackProps
{
    Env = env,
    Stage = stage,
    Vpc = network.Vpc,
    LambdaSg = network.LambdaSg,
    Db = data.Db,
    Storage = storage,
    Auth = auth,
    ApiAssetPath = apiAsset,
});
new WorkflowStack(app, Id("workflow"), new WorkflowStackProps
{
    Env = env,
    Stage = stage,
    Vpc = network.Vpc,
    LambdaSg = network.LambdaSg,
    Db = data.Db,
    FunctionsAssetPath = functionsAsset,
    IncomingBucketName = incomingBucket,
    DerivedBucketName = derivedBucket,
    EvidenceBucketName = evidenceBucket,
});
// WAF cho CloudFront phải ở us-east-1, nhưng SCP chặn us-east-1: synth được, deploy sẽ lỗi. Cần cả nhóm quyết định.
new EdgeStack(app, Id("edge"), new StackProps { Env = new Amazon.CDK.Environment { Region = "us-east-1" } });
new ObservabilityStack(app, Id("observability"), P());

Tags.Of(app).Add("Project", "RoomLedger");
Tags.Of(app).Add("Stage", stage);

app.Synth();
