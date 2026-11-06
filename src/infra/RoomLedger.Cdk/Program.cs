using Amazon.CDK;
using RoomLedger.Cdk.Stacks;

var app = new App();
var stage = (string?)app.Node.TryGetContext("stage") ?? "dev";   // dev | demo
var env = new Amazon.CDK.Environment { Region = "ap-southeast-1" };
StackProps P() => new() { Env = env };
string Id(string name) => $"roomledger-{stage}-{name}";

new NetworkStack(app, Id("network"), P());
new SecurityStack(app, Id("security"), P());
new DataStack(app, Id("data"), P());
new StorageStack(app, Id("storage"), P());
new AuthStack(app, Id("auth"), P());
new ApiStack(app, Id("api"), P());
new WorkflowStack(app, Id("workflow"), P());
new EdgeStack(app, Id("edge"), new StackProps { Env = new Amazon.CDK.Environment { Region = "us-east-1" } }); // WAF CloudFront phải ở us-east-1
new ObservabilityStack(app, Id("observability"), P());

Tags.Of(app).Add("Project", "RoomLedger");
Tags.Of(app).Add("Stage", stage);

app.Synth();
