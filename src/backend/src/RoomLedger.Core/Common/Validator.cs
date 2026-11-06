using System.Net.Mail;

namespace RoomLedger.Core.Common;

/**
 * Gom lỗi theo từng trường rồi ném một ValidationException duy nhất,
 * để FE hiện được tất cả lỗi dưới từng ô trong một lần.
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed class Validator
{
    private readonly List<FieldError> _errors = [];

    /**
     * Thêm lỗi cho trường khi điều kiện sai.
     *
     * @author Tuấn
     * @version 1.0
     */
    public Validator Require(bool condition, string field, string message)
    {
        if (!condition)
        {
            _errors.Add(new FieldError(field, message));
        }

        return this;
    }

    /**
     * Chuỗi bắt buộc, độ dài trong khoảng [min, max] sau khi bỏ khoảng trắng hai đầu.
     *
     * @author Tuấn
     * @version 1.0
     */
    public Validator Text(string? value, string field, int min, int max, string label)
    {
        var length = value?.Trim().Length ?? 0;
        return Require(length >= min && length <= max, field,
            min > 0 ? $"{label} phải có từ {min} đến {max} ký tự" : $"{label} tối đa {max} ký tự");
    }

    /**
     * Số nguyên phải lớn hơn 0.
     *
     * @author Tuấn
     * @version 1.0
     */
    public Validator Positive(long? value, string field, string label) =>
        Require(value is > 0, field, $"{label} phải lớn hơn 0");

    public bool HasErrors => _errors.Count > 0;

    /**
     * Ném ValidationException nếu có lỗi.
     *
     * @author Tuấn
     * @version 1.0
     */
    public void ThrowIfInvalid()
    {
        if (HasErrors)
        {
            throw new ValidationException(_errors.ToList());
        }
    }

    /**
     * Kiểm định dạng email (có @ và tên miền).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            return false;
        }

        try
        {
            var address = new MailAddress(email.Trim());
            return address.Address == email.Trim() && address.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
