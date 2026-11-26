using System.Net;
using System.Text.Json;
using Amazon.Lambda.CloudWatchEvents;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Functions.Shared;

/**
 * Đổi "Object Created" của bucket outbox thành sự kiện nghiệp vụ: source và detail-type lấy từ key
 * events/{source}/{detailType}/..., detail là nội dung object (S3OutboxPublisher ghi).
 *
 * @author Sang
 * @version 1.0
 */
public static class OutboxEvent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<CloudWatchEvent<T>> ReadAsync<T>(IObjectStore store, CloudWatchEvent<S3ObjectCreatedDetail> input, CancellationToken ct)
    {
        var key = WebUtility.UrlDecode(input.Detail.Object.Key);
        var parts = key.Split('/');
        if (parts.Length < 4 || parts[0] != "events")
        {
            throw new InvalidOperationException($"Key outbox không đúng dạng: {key}");
        }

        await using var body = await store.OpenReadAsync(StorageBucket.Outbox, key, ct);
        var detail = await JsonSerializer.DeserializeAsync<T>(body, Json, ct)
            ?? throw new InvalidOperationException($"Sự kiện outbox rỗng: {key}");
        return new CloudWatchEvent<T> { Source = parts[1], DetailType = parts[2], Detail = detail };
    }
}
