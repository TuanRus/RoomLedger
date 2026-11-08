using System.Collections.Concurrent;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Api.Tests.Infrastructure;

/**
 * S3 giả cho API (đọc/ghi/xóa object), để test xóa ảnh mà không gọi AWS.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class FakeObjectStore : IObjectStore
{
    public ConcurrentDictionary<(StorageBucket Bucket, string Key), byte[]> Objects { get; } = new();

    public Task<Stream> OpenReadAsync(StorageBucket bucket, string key, CancellationToken ct) =>
        Objects.TryGetValue((bucket, key), out var content)
            ? Task.FromResult<Stream>(new MemoryStream(content, writable: false))
            : throw new FileNotFoundException($"{bucket}/{key}");

    public Task PutAsync(StorageBucket bucket, string key, byte[] content, string contentType, CancellationToken ct)
    {
        Objects[(bucket, key)] = content;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(StorageBucket bucket, string key, CancellationToken ct)
    {
        Objects.TryRemove((bucket, key), out _);   // S3 xóa object không tồn tại cũng không lỗi
        return Task.CompletedTask;
    }

    public Task<string?> CopyAsync(StorageBucket from, string fromKey, StorageBucket to, string toKey, CancellationToken ct)
    {
        Objects[(to, toKey)] = Objects[(from, fromKey)];
        return Task.FromResult<string?>(Guid.NewGuid().ToString("N"));
    }
}
