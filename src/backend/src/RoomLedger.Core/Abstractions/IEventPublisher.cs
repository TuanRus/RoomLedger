namespace RoomLedger.Core.Abstractions;

public interface IEventPublisher
{
    Task PublishAsync(string detailType, object detail, CancellationToken ct);
}
