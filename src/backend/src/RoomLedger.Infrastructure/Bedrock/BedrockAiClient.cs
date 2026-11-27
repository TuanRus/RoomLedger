using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using RoomLedger.Core.Abstractions;

namespace RoomLedger.Infrastructure.Bedrock;

/**
 * Đầu vào của tác vụ đọc ảnh: JSON {"imageBase64": "...", "mediaType": "image/jpeg"}.
 *
 * @author Tuấn
 * @version 1.0
 */
public sealed record AiImageInput(string ImageBase64, string MediaType = "image/jpeg");

/**
 * IAiClient trên Amazon Bedrock (Converse API, Claude Haiku 4.5 qua inference profile au.*).
 * Id mô hình ở Parameter Store (ai/model-id) và prompt theo tác vụ ở ai/prompt/{task}; JSON schema do bên gọi
 * đưa vào (schemaFor) rồi gắn cuối prompt hệ thống để mô hình trả đúng khuôn. Nhiệt độ 0 để kết quả ổn định.
 * Trả nguyên văn câu trả lời của mô hình; bên gọi tự đọc và kiểm lại JSON.
 *
 * @author Tuấn
 * @version 1.0
 */
public class BedrockAiClient(IAmazonBedrockRuntime bedrock, IConfigProvider config, Func<string, string> schemaFor) : IAiClient
{
    public const string ModelIdParameter = "ai/model-id";
    public const int MaxOutputTokens = 400;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string PromptParameter(string task) => $"ai/prompt/{task}";

    public async Task<string> InvokeAsync(string task, string inputJson, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<AiImageInput>(inputJson, Json)
            ?? throw new ArgumentException("Đầu vào AI rỗng", nameof(inputJson));
        var modelId = (await config.GetAsync(ModelIdParameter, ct)).Trim();
        var prompt = await config.GetAsync(PromptParameter(task), ct);

        var response = await bedrock.ConverseAsync(new ConverseRequest
        {
            ModelId = modelId,
            System = [new SystemContentBlock { Text = $"{prompt.Trim()}\n\nJSON schema của kết quả:\n{schemaFor(task)}" }],
            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content =
                    [
                        new ContentBlock { Image = new ImageBlock { Format = ImageFormatFor(input.MediaType), Source = new ImageSource { Bytes = new MemoryStream(Convert.FromBase64String(input.ImageBase64)) } } },
                        new ContentBlock { Text = "Đọc chỉ số trong ảnh này." },
                    ],
                },
            ],
            InferenceConfig = new InferenceConfiguration { MaxTokens = MaxOutputTokens, Temperature = 0f },
        }, ct);

        return string.Concat(response.Output.Message.Content.Select(c => c.Text));
    }

    private static ImageFormat ImageFormatFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ImageFormat.Jpeg,
        "image/png" => ImageFormat.Png,
        "image/webp" => ImageFormat.Webp,
        _ => throw new ArgumentException($"Bedrock không nhận định dạng ảnh {mediaType}", nameof(mediaType)),
    };
}
