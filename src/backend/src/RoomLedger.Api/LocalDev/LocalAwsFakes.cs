using System.Collections.Concurrent;
using System.Text.Json;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Api.LocalDev;

/**
 * S3 thay thế khi chạy trên máy: không gọi AWS. URL tải lên/tải xuống trỏ về /api/v1/local-dev/files/{bucket}/{key}
 * (đi qua proxy của FE), file nằm trong bộ nhớ và mất khi khởi động lại API.
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed class LocalStorage : IStorage, IObjectStore
{
    private const string BaseUrl = "/api/v1/local-dev/files";
    private readonly ConcurrentDictionary<(StorageBucket Bucket, string Key), (byte[] Content, string ContentType)> _objects = new();

    public PresignedUpload CreateUploadUrl(string key, string contentType, string sha256Base64, TimeSpan lifetime) =>
        new($"{BaseUrl}/incoming/{key}", "PUT",
            new Dictionary<string, string> { ["Content-Type"] = contentType, ["x-amz-checksum-sha256"] = sha256Base64 },
            DateTimeOffset.UtcNow.Add(lifetime));

    public string CreateDownloadUrl(StorageBucket bucket, string key, TimeSpan lifetime, string? versionId = null) =>
        $"{BaseUrl}/{bucket.ToString().ToLowerInvariant()}/{key}";

    /** File đã tải lên, cho endpoint GET /local-dev/files. */
    public bool TryGet(StorageBucket bucket, string key, out byte[] content, out string contentType)
    {
        var found = _objects.TryGetValue((bucket, key), out var o);
        (content, contentType) = found ? o : ([], "");
        return found;
    }

    public Task<Stream> OpenReadAsync(StorageBucket bucket, string key, CancellationToken ct) =>
        _objects.TryGetValue((bucket, key), out var content)
            ? Task.FromResult<Stream>(new MemoryStream(content.Content, writable: false))
            : throw new FileNotFoundException($"{bucket}/{key}");

    public Task PutAsync(StorageBucket bucket, string key, byte[] content, string contentType, CancellationToken ct)
    {
        _objects[(bucket, key)] = (content, contentType);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(StorageBucket bucket, string key, CancellationToken ct)
    {
        _objects.TryRemove((bucket, key), out _);
        return Task.CompletedTask;
    }

    public Task<string?> CopyAsync(StorageBucket from, string fromKey, StorageBucket to, string toKey, CancellationToken ct)
    {
        if (_objects.TryGetValue((from, fromKey), out var content))
        {
            _objects[(to, toKey)] = content;
        }

        return Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
    }
}

/**
 * EventBridge thay thế khi chạy trên máy: ghi sự kiện ra log và giữ trong bộ nhớ, để script seed
 * đọc được token lời mời (trên AWS token chỉ đi qua email).
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed class LocalEventPublisher(ILogger<LocalEventPublisher> logger) : IEventPublisher
{
    private const int Capacity = 200;
    private readonly ConcurrentQueue<(string DetailType, JsonElement Detail)> _published = new();

    public Task PublishAsync(string detailType, object detail, CancellationToken ct)
    {
        _published.Enqueue((detailType, JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        while (_published.Count > Capacity && _published.TryDequeue(out _))
        {
        }

        logger.LogInformation("LocalDev event {DetailType}", detailType);
        return Task.CompletedTask;
    }

    /** Sự kiện mới nhất cùng loại thỏa điều kiện, hoặc null. */
    public JsonElement? Last(string detailType, Func<JsonElement, bool> match) =>
        _published.Reverse().Where(e => e.DetailType == detailType && match(e.Detail)).Select(e => (JsonElement?)e.Detail).FirstOrDefault();
}
