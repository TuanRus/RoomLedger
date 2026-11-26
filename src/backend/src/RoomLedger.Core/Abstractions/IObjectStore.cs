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

    /** Dung lượng object (HEAD, không tải về); null nếu object không tồn tại. */
    Task<long?> GetSizeAsync(StorageBucket bucket, string key, CancellationToken ct);

    Task PutAsync(StorageBucket bucket, string key, byte[] content, string contentType, CancellationToken ct);

    /** Xóa object; không có cũng không lỗi (giống S3). Không dùng cho bucket evidence (Object Lock). */
    Task DeleteAsync(StorageBucket bucket, string key, CancellationToken ct);

    /** Chép object giữa hai bucket (phía S3, không tải về); nguồn không có thì ném FileNotFoundException. Trả VersionId của bản mới (bucket bật versioning). */
    Task<string?> CopyAsync(StorageBucket from, string fromKey, StorageBucket to, string toKey, CancellationToken ct);
}
