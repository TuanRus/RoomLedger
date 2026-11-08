namespace RoomLedger.Api.Contracts;

/**
 * Body POST /properties (MD-02-FE-01).
 *
 * @author Tuấn
 * @version 1.0
 */
public record CreatePropertyRequest(string? Name, string? Address, long? ElectricityRate, long? WaterRate, long? ServiceFee);

/**
 * Nhà trọ trả về cho chủ nhà.
 *
 * @author Tuấn
 * @version 1.0
 */
public record PropertyResponse(
    Guid PropertyId,
    string Name,
    string Address,
    long ElectricityRate,
    long WaterRate,
    long ServiceFee,
    int RoomCount,
    DateTimeOffset CreatedAt);
