using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace RoomLedger.Ai.Compare;

/** Kết quả so một cặp: đã phân tích, hoặc không phân tích được (cả hai mô hình lỗi). */
public enum CompareStatus
{
    Analyzed,
    NotAnalyzed,
}

/** Khung trên ảnh trả phòng, tỉ lệ 0–1 tính từ góc trên bên trái. */
public record BoundingBox(double X, double Y, double Width, double Height);

/** Một chỗ có thể hư hỏng do mô hình chỉ ra. */
public record AiFinding(string Title, string Description, string Area, BoundingBox BoundingBox, double Confidence);

/** Kết quả CompareAsync; ModelId là mô hình đã trả lời hợp lệ (null nếu không có). */
public record CompareOutcome(CompareStatus Status, IReadOnlyList<AiFinding> Findings, string? ModelId, bool UsedFallback,
    IReadOnlyList<string> Errors);

/**
 * So ảnh nhận phòng và ảnh trả phòng bằng Amazon Bedrock (BR-26 RULE 3, EXCEPTION 2).
 * Gửi prompt compareRoomPhotos (tiếng Việt) và hai ảnh JPEG theo thứ tự nhận → trả, nhiệt độ 0, qua Converse API.
 * Câu trả lời phải là JSON đúng Schemas/compareRoomPhotos.schema.json (tối đa 10 phát hiện, khung nằm trong ảnh,
 * confidence 0–1). Mô hình chính lỗi hoặc trả sai → mô hình dự phòng; cả hai hỏng → NOT_ANALYZED, không ném lỗi
 * để không chặn quyết toán. Hủy (CancellationToken) thì ném lại, không chuyển sang dự phòng.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class RoomPhotoComparer(IAmazonBedrockRuntime bedrock, string primaryModelId, string fallbackModelId)
{
    private const int MaxFindings = 10;
    private static readonly Lazy<string> Prompt = new(() => LoadResource(".Prompts.compareRoomPhotos.md"));

    public async Task<CompareOutcome> CompareAsync(byte[] moveInJpeg, byte[] moveOutJpeg, string areaName, CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var (modelId, isFallback) in new[] { (primaryModelId, false), (fallbackModelId, true) })
        {
            try
            {
                var text = await AskAsync(modelId, moveInJpeg, moveOutJpeg, areaName, ct);
                var findings = Parse(text);
                return new CompareOutcome(CompareStatus.Analyzed, findings, modelId, isFallback, errors);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                errors.Add($"{modelId}: {e.GetType().Name}: {e.Message}");
            }
        }
        return new CompareOutcome(CompareStatus.NotAnalyzed, [], null, false, errors);
    }

    private async Task<string> AskAsync(string modelId, byte[] moveIn, byte[] moveOut, string areaName, CancellationToken ct)
    {
        var request = new ConverseRequest
        {
            ModelId = modelId,
            System = [new SystemContentBlock { Text = Prompt.Value }],
            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content =
                    [
                        new ContentBlock { Text = $"Khu vực: {areaName}. Ảnh 1 (nhận phòng):" },
                        Image(moveIn),
                        new ContentBlock { Text = "Ảnh 2 (trả phòng):" },
                        Image(moveOut),
                    ],
                },
            ],
            InferenceConfig = new InferenceConfiguration { Temperature = 0f, MaxTokens = 2000 },
        };
        var response = await bedrock.ConverseAsync(request, ct);
        return string.Concat(response.Output?.Message?.Content?.Select(c => c.Text) ?? []);
    }

    private static ContentBlock Image(byte[] jpeg) =>
        new() { Image = new ImageBlock { Format = ImageFormat.Jpeg, Source = new ImageSource { Bytes = new MemoryStream(jpeg) } } };

    /** Đọc và kiểm JSON theo schema; sai → FormatException (để chuyển sang mô hình dự phòng). */
    internal static IReadOnlyList<AiFinding> Parse(string text)
    {
        var json = text.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            json = json[(json.IndexOf('\n') + 1)..];
            var end = json.LastIndexOf("```", StringComparison.Ordinal);
            json = (end >= 0 ? json[..end] : json).Trim();
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new FormatException("Câu trả lời không phải JSON", e);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("findings", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Thiếu mảng findings");
            }
            if (items.GetArrayLength() > MaxFindings)
            {
                throw new FormatException($"Quá {MaxFindings} phát hiện");
            }
            return items.EnumerateArray().Select(ParseFinding).ToList();
        }
    }

    private static AiFinding ParseFinding(JsonElement e)
    {
        string Text(string name, int max)
        {
            var v = e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()!.Trim() : "";
            return v.Length is > 0 and var n && n <= max ? v : throw new FormatException($"{name} không hợp lệ");
        }
        static double Number(JsonElement parent, string name, double min, double max, bool minExclusive = false)
        {
            if (!parent.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number)
            {
                throw new FormatException($"Thiếu {name}");
            }
            var v = p.GetDouble();
            return (minExclusive ? v > min : v >= min) && v <= max ? v : throw new FormatException($"{name} ngoài khoảng");
        }

        if (!e.TryGetProperty("boundingBox", out var b) || b.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Thiếu boundingBox");
        }
        var box = new BoundingBox(Number(b, "x", 0, 1), Number(b, "y", 0, 1), Number(b, "width", 0, 1, true), Number(b, "height", 0, 1, true));
        if (box.X + box.Width > 1.0001 || box.Y + box.Height > 1.0001)
        {
            throw new FormatException("boundingBox ra ngoài ảnh");
        }
        return new AiFinding(Text("title", 80), Text("description", 500), Text("area", 200), box, Number(e, "confidence", 0, 1));
    }

    private static string LoadResource(string suffix)
    {
        var asm = typeof(RoomPhotoComparer).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var reader = new StreamReader(asm.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }
}
