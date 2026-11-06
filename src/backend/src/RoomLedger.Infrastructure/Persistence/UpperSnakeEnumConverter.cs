using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RoomLedger.Infrastructure.Persistence;

/**
 * Lưu enum dạng chuỗi UPPER_SNAKE_CASE (InspectionStatus.AwaitingConfirm → AWAITING_CONFIRM),
 * cùng quy tắc với JSON của API để database và API đọc giống nhau.
 *
 * @author Tuấn
 * @version 1.0
 */
public class UpperSnakeEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    v => ToDb(v),
    v => FromDb(v))
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToMap =
        Enum.GetValues<TEnum>().ToDictionary(v => v, v => JsonNamingPolicy.SnakeCaseUpper.ConvertName(v.ToString()));

    private static readonly Dictionary<string, TEnum> FromMap = ToMap.ToDictionary(kv => kv.Value, kv => kv.Key);

    /**
     * Đổi giá trị enum sang chuỗi lưu database.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string ToDb(TEnum value) => ToMap[value];

    /**
     * Đổi chuỗi trong database về enum; giá trị lạ là lỗi dữ liệu nên ném lỗi rõ ràng.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static TEnum FromDb(string value) =>
        FromMap.TryGetValue(value, out var e) ? e : throw new InvalidOperationException($"Giá trị enum không hợp lệ: {value}");
}
