using System.Text.Json;
using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;

namespace RoomLedger.Core.Services;

/**
 * Điều khoản chính của hợp đồng (tiền là VND, ngày là DateOnly).
 *
 * @author Tuấn
 * @version 1.0
 */
public record ContractTerms(
    long? MonthlyRent,
    long? Deposit,
    DateOnly? StartDate,
    DateOnly? EndDate,
    long? ElectricityRate,
    long? WaterRate);

/**
 * Giá trị AI đọc được cho một điều khoản và độ tin cậy (0–1).
 *
 * @author Tuấn
 * @version 1.0
 */
public record AiTermValue(JsonElement? Value, decimal? Confidence);

/**
 * Luật nghiệp vụ hợp đồng (BR-09 đến BR-14), không phụ thuộc database.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class TenancyService
{
    /** Trường có confidence dưới ngưỡng này được tô vàng (BR-10 RULE 4). */
    public const decimal ReviewConfidence = 0.8m;

    public const int MaxFilesPerVersion = 10;
    public const long MaxScanBytes = 20L * 1024 * 1024;
    public const int MinRejectReasonLength = 10;

    public static readonly string[] TermFields =
        ["monthlyRent", "deposit", "startDate", "endDate", "electricityRate", "waterRate"];

    /**
     * Kiểm điều khoản (BR-11 RULE 1): tiền thuê > 0, cọc ≥ 0, ngày bắt đầu trước ngày kết thúc, giá điện nước > 0.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void ValidateTerms(ContractTerms t)
    {
        new Validator()
            .Positive(t.MonthlyRent, "monthlyRent", "Tiền thuê")
            .Require(t.Deposit is >= 0, "deposit", "Tiền cọc không được âm")
            .Require(t.StartDate is not null, "startDate", "Ngày bắt đầu là bắt buộc")
            .Require(t.EndDate is not null && (t.StartDate is null || t.EndDate > t.StartDate), "endDate",
                "Ngày kết thúc phải sau ngày bắt đầu")
            .Positive(t.ElectricityRate, "electricityRate", "Giá điện")
            .Positive(t.WaterRate, "waterRate", "Giá nước")
            .ThrowIfInvalid();
    }

    /**
     * Đọc kết quả AI thô (jsonb) thành từng điều khoản. JSON hỏng thì coi như AI không đọc được.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IReadOnlyDictionary<string, AiTermValue> ParseAiTerms(string? aiTermsJson)
    {
        var result = new Dictionary<string, AiTermValue>();
        if (string.IsNullOrWhiteSpace(aiTermsJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(aiTermsJson);
            foreach (var field in TermFields)
            {
                if (!doc.RootElement.TryGetProperty(field, out var node) || node.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                JsonElement? value = node.TryGetProperty("value", out var v) && v.ValueKind != JsonValueKind.Null ? v.Clone() : null;
                decimal? confidence = node.TryGetProperty("confidence", out var c) && c.TryGetDecimal(out var d) ? d : null;
                result[field] = new AiTermValue(value, confidence);
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, AiTermValue>();
        }

        return result;
    }

    /**
     * Danh sách trường chủ nhà đã sửa so với AI (BR-11 RULE 3): AI không có giá trị hoặc giá trị khác.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static List<string> EditedFields(ContractTerms saved, IReadOnlyDictionary<string, AiTermValue> ai)
    {
        var current = TermsAsStrings(saved);
        return TermFields
            .Where(f => !ai.TryGetValue(f, out var a) || a.Value is null || Normalize(a.Value.Value) != current[f])
            .ToList();
    }

    /**
     * Áp điều khoản vào phiên bản hợp đồng.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void ApplyTerms(TenancyVersion version, ContractTerms t)
    {
        version.MonthlyRent = t.MonthlyRent;
        version.Deposit = t.Deposit;
        version.StartDate = t.StartDate;
        version.EndDate = t.EndDate;
        version.ElectricityRate = t.ElectricityRate;
        version.WaterRate = t.WaterRate;
    }

    /**
     * Lý do "Không đúng" phải có ít nhất 10 ký tự (BR-13 RULE 2).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string ValidateRejectReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length < MinRejectReasonLength || trimmed.Length > 500)
        {
            throw ValidationException.For("reason", "Lý do phải có từ 10 đến 500 ký tự");
        }

        return trimmed;
    }

    private static Dictionary<string, string?> TermsAsStrings(ContractTerms t) => new()
    {
        ["monthlyRent"] = t.MonthlyRent?.ToString(),
        ["deposit"] = t.Deposit?.ToString(),
        ["startDate"] = t.StartDate?.ToString("yyyy-MM-dd"),
        ["endDate"] = t.EndDate?.ToString("yyyy-MM-dd"),
        ["electricityRate"] = t.ElectricityRate?.ToString(),
        ["waterRate"] = t.WaterRate?.ToString(),
    };

    private static string? Normalize(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number when e.TryGetInt64(out var l) => l.ToString(),
        JsonValueKind.Number => e.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
        JsonValueKind.String => e.GetString(),
        _ => e.GetRawText(),
    };
}
