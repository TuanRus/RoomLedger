using System.Text.Json;
using RoomLedger.Core.Common;

namespace RoomLedger.Api.Contracts;

/**
 * Đọc enum từ query string theo đúng định dạng của API (UPPER_SNAKE_CASE, ví dụ ?status=AWAITING_CONFIRM).
 *
 * @author Tuấn
 * @version 1.0
 */
public static class EnumQuery
{
    /**
     * Trả null khi không truyền; giá trị lạ trả VALIDATION_ERROR.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static TEnum? Parse<TEnum>(string? value, string field) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var e in Enum.GetValues<TEnum>())
        {
            if (string.Equals(JsonNamingPolicy.SnakeCaseUpper.ConvertName(e.ToString()), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }

        throw ValidationException.For(field, $"Giá trị {field} không hợp lệ");
    }
}
