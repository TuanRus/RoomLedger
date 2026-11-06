namespace RoomLedger.Core.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Role { get; }
}
