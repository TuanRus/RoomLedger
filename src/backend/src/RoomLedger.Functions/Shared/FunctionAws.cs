using Amazon.EventBridge;
using Amazon.S3;
using Microsoft.Extensions.Options;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Services;
using RoomLedger.Infrastructure.Events;
using RoomLedger.Infrastructure.S3;

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
 * EVENT_BUS_NAME, PHOTO_MAX_AGE_DAYS.
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
    })));

    private static readonly Lazy<IEventPublisher> Publisher = new(() => new EventBridgePublisher(new AmazonEventBridgeClient(),
        Options.Create(new EventBusOptions
        {
            BusName = Environment.GetEnvironmentVariable("EVENT_BUS_NAME") ?? "default",
            Source = "roomledger.functions",
        })));

    public static IObjectStore ObjectStore => Store.Value;

    public static IEventPublisher Events => Publisher.Value;

    public static int PhotoMaxAgeDays =>
        int.TryParse(Environment.GetEnvironmentVariable("PHOTO_MAX_AGE_DAYS"), out var days) && days > 0
            ? days
            : PhotoRules.DefaultMaxAgeDays;

    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";
}
