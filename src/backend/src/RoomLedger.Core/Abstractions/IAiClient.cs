namespace RoomLedger.Core.Abstractions;

public interface IAiClient
{
    Task<string> InvokeAsync(string task, string inputJson, CancellationToken ct);
}
