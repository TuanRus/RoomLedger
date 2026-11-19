using Amazon.BedrockRuntime;
using Amazon.Lambda.Core;
using RoomLedger.Ai.Compare;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Shared;

namespace RoomLedger.Functions.ComparePairs;

/**
 * Một phần tử của bước Map trong Step Functions so ảnh trả phòng (MD-06-FE-02, BR-26 RULE 3, EXCEPTION 2).
 * Chạy ngoài VPC (VPC không có NAT nên không tới được Bedrock), không đọc database: đọc hai bản xem JPEG trong bucket
 * derived theo khóa plan-compare đưa, gọi RoomPhotoComparer (mô hình chính → dự phòng). Cả hai mô hình lỗi →
 * NOT_ANALYZED, không ném lỗi. Thiếu ảnh hoặc lỗi S3 → ném lỗi để Step Functions thử lại; hết lượt thì bước Catch ghi
 * NOT_ANALYZED. Mô hình đặt qua BEDROCK_PRIMARY_MODEL, BEDROCK_FALLBACK_MODEL (CDK).
 *
 * @author Sang
 * @version 1.0
 */
public class Function
{
    private readonly IObjectStore _store;
    private readonly RoomPhotoComparer _comparer;

    public Function() : this(FunctionAws.ObjectStore, new RoomPhotoComparer(new AmazonBedrockRuntimeClient(),
        Env("BEDROCK_PRIMARY_MODEL"), Env("BEDROCK_FALLBACK_MODEL")))
    {
    }

    public Function(IObjectStore store, RoomPhotoComparer comparer)
    {
        _store = store;
        _comparer = comparer;
    }

    public async Task<PairResult> HandleAsync(CompareTask input, ILambdaContext context)
    {
        var ct = CancellationToken.None;
        var moveIn = await ReadAsync(input.MoveInKey, ct);
        var moveOut = await ReadAsync(input.MoveOutKey, ct);

        var outcome = await _comparer.CompareAsync(moveIn, moveOut, input.AreaName, ct);
        foreach (var error in outcome.Errors)
        {
            context.Logger.LogWarning($"Ảnh {input.MoveOutPhotoId}: {error}");
        }
        var status = outcome.Status == CompareStatus.Analyzed ? PairResult.Analyzed : PairResult.NotAnalyzed;
        context.Logger.LogInformation($"Ảnh {input.MoveOutPhotoId}: {status}, {outcome.Findings.Count} phát hiện, mô hình {outcome.ModelId}");
        return new PairResult(input.MoveOutPhotoId, status, outcome.Findings, outcome.ModelId, outcome.UsedFallback);
    }

    private async Task<byte[]> ReadAsync(string key, CancellationToken ct)
    {
        await using var stream = await _store.OpenReadAsync(StorageBucket.Derived, key, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Thiếu biến môi trường {name}");
}
