using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using RoomLedger.Ai.Compare;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * So hai ảnh bằng Bedrock (BR-26 RULE 3, EXCEPTION 2): gửi ảnh nhận phòng và trả phòng cùng prompt compareRoomPhotos,
 * đọc JSON đúng schema; mô hình chính lỗi hoặc trả JSON sai → mô hình dự phòng; cả hai lỗi → NOT_ANALYZED, không ném lỗi.
 * Bedrock giả kế thừa client của SDK, không gọi mạng.
 *
 * @author Sang
 * @version 1.0
 */
public class RoomPhotoComparerTests
{
    private const string Sonnet = "au.anthropic.claude-sonnet-4-5-20250929-v1:0";
    private const string Haiku = "au.anthropic.claude-haiku-4-5-20251001-v1:0";
    private static readonly byte[] MoveIn = [0xFF, 0xD8, 1, 2, 3];
    private static readonly byte[] MoveOut = [0xFF, 0xD8, 4, 5, 6];

    private const string OneFinding = """
        {"findings":[{"title":"Vết bẩn trên tường","description":"Mảng ố vàng lớn cạnh cửa sổ.","area":"Phòng ngủ",
          "boundingBox":{"x":0.1,"y":0.2,"width":0.3,"height":0.25},"confidence":0.82}]}
        """;

    /** Bedrock giả: mỗi model id trả một câu trả lời hoặc ném lỗi; ghi lại mọi request. */
    private sealed class FakeBedrock() : AmazonBedrockRuntimeClient(new AnonymousAWSCredentials(), RegionEndpoint.APSoutheast2)
    {
        public Dictionary<string, Func<string>> Replies { get; } = new();
        public List<ConverseRequest> Requests { get; } = [];

        public override Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var text = Replies[request.ModelId]();
            return Task.FromResult(new ConverseResponse
            {
                Output = new ConverseOutput
                {
                    Message = new Message { Role = ConversationRole.Assistant, Content = [new ContentBlock { Text = text }] },
                },
                StopReason = StopReason.End_turn,
            });
        }
    }

    private static (RoomPhotoComparer Comparer, FakeBedrock Bedrock) Create(Func<string> sonnet, Func<string>? haiku = null)
    {
        var fake = new FakeBedrock();
        fake.Replies[Sonnet] = sonnet;
        fake.Replies[Haiku] = haiku ?? (() => throw new Xunit.Sdk.XunitException("không được gọi mô hình dự phòng"));
        return (new RoomPhotoComparer(fake, Sonnet, Haiku), fake);
    }

    private static Func<string> Throws(Exception e) => () => throw e;

    [Fact]
    public async Task ValidAnswer_FromThePrimaryModel_IsParsed()
    {
        var (comparer, _) = Create(() => OneFinding);

        var result = await comparer.CompareAsync(MoveIn, MoveOut, "Phòng ngủ", CancellationToken.None);

        Assert.Equal(CompareStatus.Analyzed, result.Status);
        Assert.Equal(Sonnet, result.ModelId);
        Assert.False(result.UsedFallback);
        var f = Assert.Single(result.Findings);
        Assert.Equal("Vết bẩn trên tường", f.Title);
        Assert.Equal("Mảng ố vàng lớn cạnh cửa sổ.", f.Description);
        Assert.Equal("Phòng ngủ", f.Area);
        Assert.Equal(new BoundingBox(0.1, 0.2, 0.3, 0.25), f.BoundingBox);
        Assert.Equal(0.82, f.Confidence, 3);
    }

    [Fact]
    public async Task Request_SendsBothJpegs_InOrder_TheVietnamesePrompt_AndTheAreaName_AtTemperatureZero()
    {
        var (comparer, bedrock) = Create(() => OneFinding);

        await comparer.CompareAsync(MoveIn, MoveOut, "Nhà tắm", CancellationToken.None);

        var req = Assert.Single(bedrock.Requests);
        Assert.Equal(Sonnet, req.ModelId);
        Assert.Contains("Ảnh 1: chụp lúc nhận phòng", Assert.Single(req.System).Text);
        var content = Assert.Single(req.Messages).Content;
        var images = content.Where(c => c.Image is not null).Select(c => c.Image).ToList();
        Assert.Equal(2, images.Count);
        Assert.All(images, i => Assert.Equal(ImageFormat.Jpeg, i.Format));
        Assert.Equal(MoveIn, images[0].Source.Bytes.ToArray());
        Assert.Equal(MoveOut, images[1].Source.Bytes.ToArray());
        Assert.Contains(content, c => c.Text is not null && c.Text.Contains("Nhà tắm"));
        Assert.Equal(0f, req.InferenceConfig.Temperature);
    }

    [Fact]
    public async Task NoFindings_IsAValidAnswer_AndDoesNotTriggerTheFallback()
    {
        var (comparer, _) = Create(() => """{"findings":[]}""");

        var result = await comparer.CompareAsync(MoveIn, MoveOut, "Phòng ngủ", CancellationToken.None);

        Assert.Equal(CompareStatus.Analyzed, result.Status);
        Assert.Empty(result.Findings);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task JsonInsideACodeFence_IsAccepted()
    {
        var (comparer, _) = Create(() => "```json\n" + OneFinding + "\n```");

        var result = await comparer.CompareAsync(MoveIn, MoveOut, "Phòng ngủ", CancellationToken.None);

        Assert.Single(result.Findings);
    }

    [Fact]
    public async Task PrimaryModelError_UsesTheFallbackModel()
    {
        var (comparer, bedrock) = Create(Throws(new ThrottlingException("chậm lại")), () => OneFinding);

        var result = await comparer.CompareAsync(MoveIn, MoveOut, "Phòng ngủ", CancellationToken.None);

        Assert.Equal(CompareStatus.Analyzed, result.Status);
        Assert.Equal(Haiku, result.ModelId);
        Assert.True(result.UsedFallback);
        Assert.Equal([Sonnet, Haiku], bedrock.Requests.Select(r => r.ModelId));
    }

    [Theory]
    [InlineData("Xin lỗi, tôi không thể xem ảnh này.")]                                                        // không phải JSON
    [InlineData("""{"findings":[{"title":"x","description":"y","area":"z","boundingBox":{"x":0,"y":0,"width":0.1,"height":0.1},"confidence":1.5}]}""")]  // confidence > 1
    [InlineData("""{"findings":[{"title":"x","description":"y","area":"z","boundingBox":{"x":0.9,"y":0,"width":0.5,"height":0.1},"confidence":0.5}]}""")] // khung ra ngoài ảnh
    [InlineData("""{"findings":[{"title":"","description":"y","area":"z","boundingBox":{"x":0,"y":0,"width":0.1,"height":0.1},"confidence":0.5}]}""")]   // title rỗng
    [InlineData("""{"result":[]}""")]                                                                         // thiếu findings
    [InlineData("""{"findings":[{"title":"x","description":"y","area":"z","boundingBox":{"x":0,"y":0,"width":0.1,"height":0.1},"confidence":0.5}""")]   // JSON bị cắt
    public async Task InvalidAnswer_FromThePrimaryModel_UsesTheFallbackModel(string badAnswer)
    {
        var (comparer, _) = Create(() => badAnswer, () => """{"findings":[]}""");

        var result = await comparer.CompareAsync(MoveIn, MoveOut, "Phòng ngủ", CancellationToken.None);

        Assert.Equal(CompareStatus.Analyzed, result.Status);
        Assert.True(result.UsedFallback);
    }

    [Fact]
    public async Task MoreThanTenFindings_IsInvalid()
    {
        var item = """{"title":"x","description":"y","area":"z","boundingBox":{"x":0,"y":0,"width":0.1,"height":0.1},"confidence":0.5}""";
        var eleven = "{\"findings\":[" + string.Join(",", Enumerable.Repeat(item, 11)) + "]}";
        var (comparer, _) = Create(() => eleven, () => """{"findings":[]}""");

        Assert.True((await comparer.CompareAsync(MoveIn, MoveOut, "Phòng ngủ", CancellationToken.None)).UsedFallback);
    }

    [Fact]
    public async Task BothModelsFail_IsNotAnalyzed_WithoutThrowing()
    {
        var (comparer, _) = Create(Throws(new AmazonBedrockRuntimeException("hỏng")), () => "không phải JSON");

        var result = await comparer.CompareAsync(MoveIn, MoveOut, "Phòng ngủ", CancellationToken.None);

        Assert.Equal(CompareStatus.NotAnalyzed, result.Status);
        Assert.Empty(result.Findings);
        Assert.Null(result.ModelId);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var (comparer, _) = Create(() => OneFinding);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => comparer.CompareAsync(MoveIn, MoveOut, "Phòng ngủ", cts.Token));
    }
}
