using System.Globalization;

namespace RoomLedger.Core.Common;

/**
 * Kỳ tính tiền dạng YYYY-MM.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class Period
{
    /**
     * Kiểm tra và chuẩn hóa chuỗi kỳ YYYY-MM; sai định dạng ném VALIDATION_ERROR.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string Parse(string? value, string field = "period")
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            throw ValidationException.For(field, "Kỳ phải có dạng YYYY-MM");
        }

        return d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    /**
     * Ngày đầu tiên sau khi kỳ kết thúc (ví dụ 2026-11 → 2026-12-01).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static DateOnly EndExclusive(string period) =>
        DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(1);
}
