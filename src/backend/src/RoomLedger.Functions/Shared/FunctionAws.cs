using Amazon.BedrockRuntime;
using Amazon.CognitoIdentityProvider;
using Amazon.SimpleEmailV2;
using Amazon.S3;
using Amazon.SimpleSystemsManagement;
using RoomLedger.Ai;
using Microsoft.Extensions.Options;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Services;
using RoomLedger.Infrastructure.Bedrock;
using RoomLedger.Infrastructure.Config;
using RoomLedger.Infrastructure.Events;
using RoomLedger.Infrastructure.S3;
using RoomLedger.Infrastructure.Ses;

namespace RoomLedger.Functions.Shared;

/**
 * Chi tiết sự kiện ảnh trên EventBridge (photo-ingest phát, photo-derive nhận).
 *
 * @author Sang
 * @version 1.0
 */
public sealed record PhotoEventDetail(Guid PhotoId)
{
    public const string PhotoReceived = "PhotoReceived";
}

/**
 * Client AWS thật cho Lambda, tạo một lần mỗi môi trường chạy (dùng lại giữa các lần gọi).
 * Cấu hình qua biến môi trường do CDK (WorkflowStack) đặt: INCOMING_BUCKET, DERIVED_BUCKET, EVIDENCE_BUCKET,
 * OUTBOX_BUCKET, PHOTO_MAX_AGE_DAYS; AI_PARAM_PREFIX (tiền tố Parameter Store, ví dụ /roomledger/dev) cho read-document;
 * SES_SENDER, WEB_ORIGIN, USER_POOL_ID cho notify.
 *
 * @author Sang
 * @version 1.0
 */
public static class FunctionAws
{
    private static readonly Lazy<IObjectStore> Store = new(() => new S3ObjectStore(new AmazonS3Client(), Options.Create(new StorageOptions
    {
        IncomingBucket = Env("INCOMING_BUCKET"),
        DerivedBucket = Env("DERIVED_BUCKET"),
        EvidenceBucket = Env("EVIDENCE_BUCKET"),
        OutboxBucket = Env("OUTBOX_BUCKET"),
    })));

    private static readonly Lazy<IEventPublisher> Publisher = new(() => new S3OutboxPublisher(new AmazonS3Client(),
        Options.Create(new OutboxOptions
        {
            Bucket = Env("OUTBOX_BUCKET"),
            Source = "roomledger.functions",
        })));

    // Bedrock: giới hạn thời gian dưới timeout của Lambda read-document (60 s) để lỗi mạng nổi lên cho state machine thử lại.
    private static readonly Lazy<IAiClient> AiClient = new(() => new BedrockAiClient(
        new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig { Timeout = TimeSpan.FromSeconds(45), MaxErrorRetry = 1 }),
        new SsmConfigProvider(new AmazonSimpleSystemsManagementClient(), Env("AI_PARAM_PREFIX")),
        AiTasks.Schema));

    private static readonly Lazy<INotifier> SesClient = new(() => new SesNotifier(new AmazonSimpleEmailServiceV2Client(),
        new SesOptions(Env("SES_SENDER"), Env("WEB_ORIGIN"))));

    private static readonly Lazy<IUserEmailLookup> Emails = new(() =>
        new CognitoUserEmailLookup(new AmazonCognitoIdentityProviderClient(), Env("USER_POOL_ID")));

    public static IObjectStore ObjectStore => Store.Value;

    public static INotifier Notifier => SesClient.Value;

    public static IUserEmailLookup UserEmails => Emails.Value;

    public static IAiClient Ai => AiClient.Value;

    public static IEventPublisher Events => Publisher.Value;

    public static int PhotoMaxAgeDays =>
        int.TryParse(Environment.GetEnvironmentVariable("PHOTO_MAX_AGE_DAYS"), out var days) && days > 0
            ? days
            : PhotoRules.DefaultMaxAgeDays;

    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";
}
