using System.Text.Json;
using Amazon.Lambda.Core;
using RoomLedger.Ai;
using RoomLedger.Ai.Imaging;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Bedrock;

namespace RoomLedger.Functions.ReadDocument;

/**
 * AI đọc đồng hồ điện nước (ngoài VPC: Bedrock không có endpoint trong VPC, Lambda này cũng không chạm RDS).
 * Bước giữa của state machine đọc đồng hồ: lấy ảnh từ bucket incoming, thu về cạnh dài 1568 px, hỏi Bedrock
 * (Claude Haiku 4.5, prompt và id mô hình ở Parameter Store), trả kết quả đã kiểm. Hợp đồng không dùng AI.
 *
 * Lỗi Bedrock (quá tải, hết hạn mức) được ném ra để state machine thử lại; câu trả lời sai khuôn JSON không phải
 * lỗi hệ thống nên trả readable = false (chủ nhà sẽ kiểm tra tay), không thử lại.
 *
 * @author Tuấn
 * @version 1.0
 */
public class Function
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IObjectStore _store;
    private readonly IAiClient _ai;

    public Function() : this(FunctionAws.ObjectStore, FunctionAws.Ai)
    {
    }

    public Function(IObjectStore store, IAiClient ai)
    {
        _store = store;
        _ai = ai;
    }

    public async Task<ReadDocumentOutput> HandleAsync(ReadDocumentInput input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        context.Logger.LogInformation($"ReadDocument {input.Task} cho chỉ số {input.ReadingId}");

        byte[] image;
        await using (var original = await _store.OpenReadAsync(StorageBucket.Incoming, input.Key, ct))
        {
            image = PhotoImaging.CreateForAi(original);
        }

        var request = JsonSerializer.Serialize(new AiImageInput(Convert.ToBase64String(image)), Json);
        var answer = await _ai.InvokeAsync(input.Task, request, ct);
        var result = MeterAiResult.Parse(answer);
        if (result is null)
        {
            // Không ghi nội dung trả lời vào log: có thể chứa dữ liệu ngoài khuôn
            context.Logger.LogWarning($"Chỉ số {input.ReadingId}: mô hình trả lời không đúng khuôn JSON ({answer.Length} ký tự)");
            return new ReadDocumentOutput(input.ReadingId, input.Key, false, null, 0m, "AI trả lời không đúng khuôn");
        }

        context.Logger.LogInformation($"Chỉ số {input.ReadingId}: readable={result.Readable}, confidence={result.Confidence}");
        return new ReadDocumentOutput(input.ReadingId, input.Key, result.Readable, result.Value, result.Confidence, result.Note);
    }
}
