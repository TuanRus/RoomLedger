using System.Text;
using Amazon.Lambda.TestUtilities;
using RoomLedger.Ai.Compare;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Shared;
using RoomLedger.Functions.Tests.Ai;
using RoomLedger.Functions.Tests.Photos;
using PairsFunction = RoomLedger.Functions.ComparePairs.Function;

namespace RoomLedger.Functions.Tests.Compare;

/**
 * Lambda compare-pairs (MD-06-FE-02, BR-26 RULE 3, EXCEPTION 2): một phần tử của bước Map. Chạy ngoài VPC (gọi Bedrock),
 * không đọc database: đọc hai bản xem JPEG trong bucket derived theo khóa plan-compare đưa, gọi RoomPhotoComparer.
 * Bedrock lỗi cả hai mô hình → NOT_ANALYZED, không ném lỗi. Thiếu ảnh trong S3 → ném lỗi để Step Functions thử lại,
 * hết lượt thì bước Catch ghi NOT_ANALYZED.
 *
 * @author Sang
 * @version 1.0
 */
public class ComparePairsTests
{
    private const string Sonnet = "au.anthropic.claude-sonnet-4-5-20250929-v1:0";
    private const string Haiku = "au.anthropic.claude-haiku-4-5-20251001-v1:0";
    private const string OneFinding = """
        {"findings":[{"title":"Vết bẩn trên tường","description":"Mảng ố vàng lớn cạnh cửa sổ.","area":"Bếp",
          "boundingBox":{"x":0.1,"y":0.2,"width":0.3,"height":0.25},"confidence":0.82}]}
        """;

    private readonly FakeObjectStore _store = new();
    private readonly FakeBedrock _bedrock = new();
    private readonly CompareTask _task = new(Guid.NewGuid(), Guid.NewGuid(), "Bếp", "l/t/in/a.jpg", "l/t/out/b.jpg");

    private PairsFunction Create()
    {
        _store.Objects[(StorageBucket.Derived, _task.MoveInKey)] = (Encoding.UTF8.GetBytes("anh-nhan"), "image/jpeg");
        _store.Objects[(StorageBucket.Derived, _task.MoveOutKey)] = (Encoding.UTF8.GetBytes("anh-tra"), "image/jpeg");
        return new PairsFunction(_store, new RoomPhotoComparer(_bedrock, Sonnet, Haiku));
    }

    [Fact]
    public async Task SendsBothViewJpegs_MoveInFirst_AndReturnsTheFindings()
    {
        var fn = Create();
        _bedrock.Replies[Sonnet] = () => OneFinding;

        var result = await fn.HandleAsync(_task, new TestLambdaContext());

        Assert.Equal(_task.MoveOutPhotoId, result.MoveOutPhotoId);
        Assert.Equal(PairResult.Analyzed, result.Status);
        Assert.Equal(Sonnet, result.ModelId);
        Assert.False(result.UsedFallback);
        Assert.Equal("Vết bẩn trên tường", Assert.Single(result.Findings).Title);
        var images = _bedrock.Requests.Single().Messages[0].Content.Where(c => c.Image is not null)
            .Select(c => Encoding.UTF8.GetString(c.Image.Source.Bytes.ToArray())).ToList();
        Assert.Equal(["anh-nhan", "anh-tra"], images);
        Assert.Contains(_bedrock.Requests.Single().Messages[0].Content, c => c.Text?.Contains("Bếp") == true);
    }

    [Fact]
    public async Task FallbackModelAnswer_IsMarked()
    {
        var fn = Create();
        _bedrock.Replies[Sonnet] = () => throw new TimeoutException("chậm");
        _bedrock.Replies[Haiku] = () => """{"findings":[]}""";

        var result = await fn.HandleAsync(_task, new TestLambdaContext());

        Assert.Equal(PairResult.Analyzed, result.Status);
        Assert.Equal(Haiku, result.ModelId);
        Assert.True(result.UsedFallback);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task BothModelsFail_NotAnalyzed_WithoutThrowing()
    {
        var fn = Create();
        _bedrock.Replies[Sonnet] = () => throw new TimeoutException("chậm");
        _bedrock.Replies[Haiku] = () => "không phải JSON";

        var result = await fn.HandleAsync(_task, new TestLambdaContext());

        Assert.Equal(PairResult.NotAnalyzed, result.Status);
        Assert.Empty(result.Findings);
        Assert.Null(result.ModelId);
    }

    [Fact]
    public async Task MissingViewJpeg_Throws_SoStepFunctionsRetries()
    {
        var fn = Create();
        _store.Objects.TryRemove((StorageBucket.Derived, _task.MoveOutKey), out _);

        await Assert.ThrowsAsync<FileNotFoundException>(() => fn.HandleAsync(_task, new TestLambdaContext()));
        Assert.Empty(_bedrock.Requests);
    }
}
