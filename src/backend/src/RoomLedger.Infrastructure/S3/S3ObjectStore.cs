using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Infrastructure.S3;

/**
 * IObjectStore trên S3 cho Lambda trong VPC (đi qua S3 Gateway endpoint).
 *
 * @author Sang
 * @version 1.0
 */
public class S3ObjectStore(IAmazonS3 s3, IOptions<StorageOptions> options) : IObjectStore
{
    private readonly StorageOptions _options = options.Value;

    /** Đọc hết vào bộ nhớ (ảnh ≤ 25 MB) để có stream đọc lại được (đọc EXIF rồi giải mã). */
    public async Task<Stream> OpenReadAsync(StorageBucket bucket, string key, CancellationToken ct)
    {
        using var response = await s3.GetObjectAsync(BucketName(bucket), key, ct);
        var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return buffer;
    }

    /** Ghi kèm checksum SHA-256 để S3 kiểm toàn vẹn khi nhận. */
    public async Task PutAsync(StorageBucket bucket, string key, byte[] content, string contentType, CancellationToken ct)
    {
        using var body = new MemoryStream(content, writable: false);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName(bucket),
            Key = key,
            InputStream = body,
            ContentType = contentType,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
        }, ct);
    }

    public Task DeleteAsync(StorageBucket bucket, string key, CancellationToken ct) =>
        s3.DeleteObjectAsync(BucketName(bucket), key, ct);

    private string BucketName(StorageBucket bucket) => bucket switch
    {
        StorageBucket.Incoming => _options.IncomingBucket,
        StorageBucket.Evidence => _options.EvidenceBucket,
        _ => _options.DerivedBucket,
    };
}
