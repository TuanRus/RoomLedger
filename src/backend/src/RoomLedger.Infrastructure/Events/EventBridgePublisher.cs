using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Microsoft.Extensions.Options;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Infrastructure.Events;

/**
 * Cấu hình EventBridge: tên bus và source.
 *
 * @author Tuấn
 * @version 1.0
 */
public class EventBusOptions
{
    public string BusName { get; set; } = "default";
    public string Source { get; set; } = "roomledger.api";
}

/**
 * Phát sự kiện nghiệp vụ lên EventBridge qua Interface endpoint trong VPC.
 *
 * @author Tuấn
 * @version 1.1
 */
public class EventBridgePublisher(IAmazonEventBridge client, IOptions<EventBusOptions> options) : IEventPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /**
     * Gửi một sự kiện; lỗi từ EventBridge được ném ra để request không báo thành công giả.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task PublishAsync(string detailType, object detail, CancellationToken ct)
    {
        var response = await client.PutEventsAsync(new PutEventsRequest
        {
            Entries =
            [
                new PutEventsRequestEntry
                {
                    EventBusName = options.Value.BusName,
                    Source = options.Value.Source,
                    DetailType = detailType,
                    Detail = JsonSerializer.Serialize(detail, Json),
                },
            ],
        }, ct);

        if (response.FailedEntryCount > 0)
        {
            throw new InvalidOperationException($"EventBridge từ chối sự kiện {detailType}: {response.Entries[0].ErrorCode}");
        }
    }
}
