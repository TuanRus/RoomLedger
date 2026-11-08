using System.Text.Json.Serialization;

namespace RoomLedger.Functions.Shared;

/**
 * Phần detail của sự kiện "Object Created" mà S3 gửi qua EventBridge (bucket incoming bật EventBridge notifications).
 * Chỉ lấy các trường photo-ingest cần.
 *
 * @author Sang
 * @version 1.0
 */
public sealed record S3ObjectCreatedDetail(
    S3BucketRef Bucket,
    [property: JsonPropertyName("object")] S3ObjectRef Object);

public sealed record S3BucketRef(string Name);

public sealed record S3ObjectRef(string Key, long Size);
