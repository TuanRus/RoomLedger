using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Infrastructure.S3;

/**
 * Tạo presigned URL S3. Ký URL chạy tại chỗ, không gọi mạng, nên Lambda API trong VPC vẫn dùng được.
 *
 * @author Tuấn
 * @version 1.1
 */
public class S3Storage(IAmazonS3 s3, IOptions<StorageOptions> options) : IStorage
{
    private readonly StorageOptions _options = options.Value;

    /**
     * Presigned PUT tới bucket incoming. x-amz-checksum-sha256 nằm trong chữ ký,
     * nên trình duyệt phải gửi đúng giá trị này và S3 tự từ chối file bị sai checksum.
     *
     * @author Tuấn
     * @version 1.1
     */
    public PresignedUpload CreateUploadUrl(string key, string contentType, string sha256Base64, TimeSpan lifetime)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.IncomingBucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            ContentType = contentType,
            Protocol = Protocol.HTTPS,
        };
        request.Headers["x-amz-checksum-sha256"] = sha256Base64;
        request.Headers["x-amz-sdk-checksum-algorithm"] = "SHA256";

        var url = s3.GetPreSignedURL(request);
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = contentType,
            ["x-amz-checksum-sha256"] = sha256Base64,
            ["x-amz-sdk-checksum-algorithm"] = "SHA256",
        };
        return new PresignedUpload(url, "PUT", headers, expiresAt);
    }

    /**
     * Presigned GET có hạn (mặc định 5 phút ở tầng gọi) cho ảnh, scan, PDF.
     *
     * @author Tuấn
     * @version 1.0
     */
    public string CreateDownloadUrl(StorageBucket bucket, string key, TimeSpan lifetime, string? versionId = null)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket switch
            {
                StorageBucket.Incoming => _options.IncomingBucket,
                StorageBucket.Evidence => _options.EvidenceBucket,
                _ => _options.DerivedBucket,
            },
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime),
            Protocol = Protocol.HTTPS,
            VersionId = versionId,
        };
        return s3.GetPreSignedURL(request);
    }
}
