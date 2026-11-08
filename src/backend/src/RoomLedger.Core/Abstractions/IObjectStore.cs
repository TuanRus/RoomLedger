namespace RoomLedger.Core.Abstractions;

/**
 * Đọc và ghi object S3 từ Lambda (photo-ingest, photo-derive, evidence-seal). Khác IStorage (chỉ tạo presigned URL cho FE).
 *
 * @author Sang
 * @version 1.0
 */
public interface IObjectStore
{
    Task<Stream> OpenReadAsync(StorageBucket bucket, string key, CancellationToken ct);

    Task PutAsync(StorageBucket bucket, string key, byte[] content, string contentType, CancellationToken ct);
}
