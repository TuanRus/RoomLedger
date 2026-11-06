using System.Collections.Concurrent;
using System.Text.Json;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Api.Tests.Infrastructure;

/**
 * S3 giả: trả URL có chứa key để test kiểm được key không lộ tên người hay số phòng.
 *
 * @author Tuấn
 * @version 1.0
 */
public class FakeStorage : IStorage
{
    public PresignedUpload CreateUploadUrl(string key, string contentType, string sha256Base64, TimeSpan lifetime) =>
        new($"https://incoming.example/{key}", "PUT",
            new Dictionary<string, string> { ["Content-Type"] = contentType, ["x-amz-checksum-sha256"] = sha256Base64 },
            DateTimeOffset.UtcNow.Add(lifetime));

    public string CreateDownloadUrl(StorageBucket bucket, string key, TimeSpan lifetime, string? versionId = null) =>
        $"https://{bucket.ToString().ToLowerInvariant()}.example/{key}";
}

/**
 * EventBridge giả: ghi lại sự kiện để test đọc token mời hoặc kiểm sự kiện đã phát.
 *
 * @author Tuấn
 * @version 1.0
 */
public class FakeEventPublisher : IEventPublisher
{
    public ConcurrentQueue<(string DetailType, JsonElement Detail)> Published { get; } = new();

    public Task PublishAsync(string detailType, object detail, CancellationToken ct)
    {
        Published.Enqueue((detailType, JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        return Task.CompletedTask;
    }

    public JsonElement Last(string detailType) => Published.Last(e => e.DetailType == detailType).Detail;
}
