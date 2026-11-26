using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Infrastructure.Events;

/**
 * Cấu hình outbox sự kiện: bucket và source (roomledger.api, roomledger.functions).
 *
 * @author Tuấn
 * @version 1.0
 */
public class OutboxOptions
{
    public string Bucket { get; set; } = "";
    public string Source { get; set; } = "roomledger.api";
}

/**
 * Phát sự kiện nghiệp vụ qua bucket outbox (sơ đồ final): ghi detail JSON vào S3 qua Gateway endpoint, bucket gửi
 * "Object Created" lên EventBridge, nên VPC không cần Interface endpoint của EventBridge.
 * Key events/{source}/{detailType}/{ngày}/{guid}.json: rule lọc theo tiền tố, IAM giới hạn mỗi hàm theo source.
 *
 * @author Tuấn
 * @version 1.0
 */
public class S3OutboxPublisher(IAmazonS3 s3, IOptions<OutboxOptions> options) : IEventPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /**
     * Ghi một sự kiện; lỗi từ S3 được ném ra để request không báo thành công giả.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task PublishAsync(string detailType, object detail, CancellationToken ct)
    {
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.Value.Bucket,
            Key = $"events/{options.Value.Source}/{detailType}/{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid():N}.json",
            ContentBody = JsonSerializer.Serialize(detail, Json),
            ContentType = "application/json",
        }, ct);
    }
}
