using System.Text.RegularExpressions;
using RoomLedger.Core.Common;

namespace RoomLedger.Core.Services;

/**
 * Luật thông tin chuyển khoản của nhà trọ: kiểm dữ liệu và điền nội dung chuyển khoản cho hóa đơn, không phụ thuộc database.
 *
 * @author Tuấn
 * @version 1.0
 */
public static partial class PaymentRules
{
    public const string DefaultTemplate = "{room} T{month}";
    public const int MaxTemplateLength = 100;
    public const long MaxQrBytes = 2L * 1024 * 1024;

    private static readonly string[] Placeholders = ["{room}", "{month}", "{year}", "{period}"];

    [GeneratedRegex(@"^[0-9 ]+$")]
    private static partial Regex DigitsAndSpaces();

    /**
     * Kiểm dữ liệu PUT payment-info: ngân hàng và chủ tài khoản 1–100 ký tự, số tài khoản chỉ gồm số và khoảng trắng (6–20 số),
     * mẫu nội dung ≤ 100 ký tự và chỉ dùng {room} {month} {year} {period}.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void ValidatePaymentInfo(string? bankName, string? accountNumber, string? accountHolder, string? template)
    {
        var digits = accountNumber?.Count(char.IsAsciiDigit) ?? 0;
        var v = new Validator()
            .Text(bankName, "bankName", 1, 100, "Tên ngân hàng")
            .Require(accountNumber is not null && DigitsAndSpaces().IsMatch(accountNumber.Trim()) && digits is >= 6 and <= 20,
                "accountNumber", "Số tài khoản chỉ gồm chữ số (6 đến 20 số)")
            .Text(accountHolder, "accountHolder", 1, 100, "Tên chủ tài khoản")
            .Require(template is null || template.Trim().Length <= MaxTemplateLength, "transferNoteTemplate",
                $"Nội dung chuyển khoản tối đa {MaxTemplateLength} ký tự")
            .Require(template is null || OnlyKnownPlaceholders(template), "transferNoteTemplate",
                "Nội dung chuyển khoản chỉ dùng {room}, {month}, {year}, {period}");
        v.ThrowIfInvalid();
    }

    /** Số tài khoản lưu chỉ gồm chữ số (bỏ khoảng trắng) để nút sao chép dán thẳng vào app ngân hàng được. */
    public static string NormalizeAccountNumber(string accountNumber) => new(accountNumber.Where(char.IsAsciiDigit).ToArray());

    /** Mẫu rỗng coi như chưa đặt (dùng mẫu mặc định). */
    public static string? NormalizeTemplate(string? template) => string.IsNullOrWhiteSpace(template) ? null : template.Trim();

    /**
     * Điền mẫu cho một hóa đơn: {room} là mã phòng bỏ dấu chấm và khoảng trắng (P.204 → P204),
     * {month} là tháng 2 chữ số, {year} là năm 4 chữ số, {period} là YYYY-MM.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string FillTransferNote(string? template, string roomCode, string period)
    {
        var room = new string(roomCode.Where(c => c != '.' && !char.IsWhiteSpace(c)).ToArray());
        return (string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template)
            .Replace("{room}", room)
            .Replace("{month}", period[5..7])
            .Replace("{year}", period[..4])
            .Replace("{period}", period);
    }

    private static bool OnlyKnownPlaceholders(string template)
    {
        var rest = template;
        foreach (var p in Placeholders)
        {
            rest = rest.Replace(p, "");
        }

        return !rest.Contains('{') && !rest.Contains('}');
    }
}
