namespace RoomLedger.Core.Abstractions;

/**
 * Phát sự kiện nghiệp vụ lên EventBridge. DetailType là PascalCase thì quá khứ (InviteCreated, InvoiceSent).
 *
 * @author Tuấn
 * @version 1.0
 */
public interface IEventPublisher
{
    Task PublishAsync(string detailType, object detail, CancellationToken ct);
}
