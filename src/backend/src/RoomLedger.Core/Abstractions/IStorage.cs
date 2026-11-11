namespace RoomLedger.Core.Abstractions;

/**
 * Bucket S3 theo mục đích (roomledger-{stage}-{mục đích}-{accountId}).
 *
 * @author Tuấn
 * @version 1.0
 */
public enum StorageBucket
{
    Incoming,
    Evidence,
    Derived,
}

/**
 * Presigned URL để trình duyệt tải file thẳng lên S3, kèm các header phải gửi đúng.
 *
 * @author Tuấn
 * @version 1.0
 */
public record PresignedUpload(string Url, string Method, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt);

/**
 * Truy cập S3 qua presigned URL. File không bao giờ đi qua Lambda API.
 *
 * @author Tuấn
 * @version 1.1
 */
public interface IStorage
{
    /**
     * Tạo presigned PUT tới bucket incoming, bắt buộc header x-amz-checksum-sha256 (base64) để S3 tự so checksum.
     *
     * @author Tuấn
     * @version 1.1
     */
    PresignedUpload CreateUploadUrl(string key, string contentType, string sha256Base64, TimeSpan lifetime);

    /**
     * Tạo presigned GET có hạn để xem ảnh hoặc file. Không trả khóa S3 ra ngoài.
     *
     * @author Tuấn
     * @version 1.0
     */
    string CreateDownloadUrl(StorageBucket bucket, string key, TimeSpan lifetime, string? versionId = null);
}
