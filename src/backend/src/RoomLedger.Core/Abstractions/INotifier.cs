namespace RoomLedger.Core.Abstractions;

public interface INotifier
{
    Task SendAsync(string templateId, string toEmail, object model, CancellationToken ct);
}
