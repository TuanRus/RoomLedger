using System.Buffers.Binary;
using System.Security.Cryptography;

namespace RoomLedger.Core.Common;

/**
 * Sinh UUID v7 (RFC 9562): 48 bit thời gian ms + ngẫu nhiên, sắp xếp được theo thời gian.
 * .NET 8 chưa có Guid.CreateVersion7() (có từ .NET 9) nên tự sinh theo cùng định dạng.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class UuidV7
{
    /**
     * Sinh UUID v7 theo thời gian hiện tại.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static Guid NewGuid() => NewGuid(DateTimeOffset.UtcNow);

    /**
     * Sinh UUID v7 với mốc thời gian cho trước (dùng trong test).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static Guid NewGuid(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        var ms = timestamp.ToUnixTimeMilliseconds();
        Span<byte> msBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(msBytes, ms);
        msBytes[2..].CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        // bigEndian: true để chuỗi UUID (và thứ tự so sánh trong PostgreSQL) giữ đúng thứ tự thời gian
        return new Guid(bytes, bigEndian: true);
    }
}
