using System.Text.Json;

namespace RoomLedger.Ai;

/**
 * Kết quả AI đọc một đồng hồ. Value chỉ có khi đọc được; Confidence luôn trong [0, 1].
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed record MeterAiResult(bool Readable, long? Value, decimal Confidence, string Note)
{
    /**
     * Đọc JSON mô hình trả về. Chấp nhận văn bản bao quanh (rào markdown, lời dẫn) bằng cách lấy đối tượng JSON
     * đầu tiên; null nếu không tìm được đối tượng hợp lệ. Chỉ số âm, thập phân hoặc không phải số → không đọc được.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static MeterAiResult? Parse(string? text)
    {
        var json = ExtractObject(text);
        if (json is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var readable = root.TryGetProperty("readable", out var r) && r.ValueKind == JsonValueKind.True;
            long? value = null;
            if (readable && root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number
                && v.TryGetInt64(out var parsed) && parsed >= 0)
            {
                value = parsed;
            }

            var confidence = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out var d)
                ? Math.Clamp(d, 0m, 1m)
                : 0m;
            var note = root.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            return value is null
                ? new MeterAiResult(false, null, confidence, note)
                : new MeterAiResult(true, value, confidence, note);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }
}
