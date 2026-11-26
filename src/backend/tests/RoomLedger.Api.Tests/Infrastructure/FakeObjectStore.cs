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

    public Task<long?> GetSizeAsync(StorageBucket bucket, string key, CancellationToken ct) =>
        Task.FromResult(Objects.TryGetValue((bucket, key), out var content) ? (long?)content.Length : null);

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

    /** Chạy trước khi chép, để test chen một yêu cầu khác vào giữa (mô phỏng hai request cùng lúc). */
    public Func<Task>? BeforeCopy { get; set; }

    public async Task<string?> CopyAsync(StorageBucket from, string fromKey, StorageBucket to, string toKey, CancellationToken ct)
    {
        if (BeforeCopy is not null)
        {
            await BeforeCopy();
        }

        Objects[(to, toKey)] = Objects.TryGetValue((from, fromKey), out var content) ? content : throw new FileNotFoundException($"{from}/{fromKey}");
        return Guid.NewGuid().ToString("N");
    }
}
