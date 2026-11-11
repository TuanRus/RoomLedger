using System.Text;
using System.Text.Json;
using RoomLedger.Core.Common;

namespace RoomLedger.Api.Contracts;

/**
 * Cursor phân trang keyset theo (created_at, id). FE coi cursor là chuỗi mờ, không tự tạo.
 *
 * @author Tuấn
 * @version 1.0
 */
public record PageCursor(DateTimeOffset CreatedAt, Guid Id)
{
    /**
     * Mã hóa cursor thành base64url.
     *
     * @author Tuấn
     * @version 1.0
     */
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { c = CreatedAt.UtcDateTime, i = Id })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /**
     * Giải mã cursor; chuỗi hỏng trả VALIDATION_ERROR.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static PageCursor? Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        try
        {
            var s = cursor.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(s));
            return new PageCursor(
                new DateTimeOffset(doc.RootElement.GetProperty("c").GetDateTime(), TimeSpan.Zero),
                doc.RootElement.GetProperty("i").GetGuid());
        }
        catch (Exception e) when (e is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw ValidationException.For("cursor", "Cursor không hợp lệ");
        }
    }
}

/**
 * Tham số phân trang chung ?limit=&cursor= (limit mặc định 20, tối đa 100).
 *
 * @author Tuấn
 * @version 1.0
 */
public static class Paging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    /**
     * Kiểm limit trong khoảng 1–100.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static int NormalizeLimit(int? limit)
    {
        if (limit is null)
        {
            return DefaultLimit;
        }

        if (limit is < 1 or > MaxLimit)
        {
            throw ValidationException.For("limit", $"limit phải từ 1 đến {MaxLimit}");
        }

        return limit.Value;
    }

    /**
     * Cắt trang: truy vấn lấy limit + 1 bản ghi; có bản thừa nghĩa là còn trang sau.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static PagedResult<TOut> ToPage<TIn, TOut>(IReadOnlyList<TIn> rows, int limit, Func<TIn, PageCursor> cursorOf, Func<TIn, TOut> map)
    {
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).ToList();
        var next = hasMore ? cursorOf(items[^1]).Encode() : null;
        return new PagedResult<TOut>(items.Select(map).ToList(), next);
    }
}
