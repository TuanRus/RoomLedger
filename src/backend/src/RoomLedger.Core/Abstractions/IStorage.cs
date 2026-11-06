namespace RoomLedger.Core.Abstractions;

public interface IStorage
{
    Task<string> CreateUploadUrlAsync(string key, string sha256Base64, CancellationToken ct);
}
