using System.Text.Json;
using Amazon.Lambda.TestUtilities;
using RoomLedger.Core.Abstractions;
using RoomLedger.Functions.Tests.Photos;
using RoomLedger.Functions.Shared;
using RoomLedger.Infrastructure.Bedrock;
using ReadFunction = RoomLedger.Functions.ReadDocument.Function;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * Lambda read-document: ảnh trong bucket incoming → thu nhỏ → AI → kết quả đã kiểm.
 *
 * @author Tuấn
 * @version 1.0
 */
public class ReadDocumentTests
{
    private sealed class FakeAi(Func<string, string, string> answer) : IAiClient
    {
        public List<(string Task, string InputJson)> Calls { get; } = [];

        public Task<string> InvokeAsync(string task, string inputJson, CancellationToken ct)
        {
            Calls.Add((task, inputJson));
            return Task.FromResult(answer(task, inputJson));
        }
    }

    private const string Key = "landlord/tenancy/reading/file.jpg";

    private static (ReadFunction Function, FakeAi Ai) Build(string answer)
    {
        var store = new FakeObjectStore();
        store.Objects[(StorageBucket.Incoming, Key)] = (SampleImages.JpegWithGps(), "image/jpeg");
        var ai = new FakeAi((_, _) => answer);
        return (new ReadFunction(store, ai), ai);
    }

    private static ReadDocumentInput Input(string task = "readElectricMeter") => new(Guid.NewGuid(), Key, task);

    [Fact]
    public async Task ReadablePhoto_SendsAShrunkJpegToTheModel_AndReturnsTheValue()
    {
        var (function, ai) = Build("""{"readable":true,"value":4821,"confidence":0.95,"note":""}""");
        var input = Input("readWaterMeter");

        var output = await function.HandleAsync(input, new TestLambdaContext());

        Assert.Equal(new ReadDocumentOutput(input.ReadingId, Key, true, 4821, 0.95m, ""), output);
        var call = Assert.Single(ai.Calls);
        Assert.Equal("readWaterMeter", call.Task);
        var sent = JsonSerializer.Deserialize<AiImageInput>(call.InputJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("image/jpeg", sent.MediaType);
        Assert.True(Convert.FromBase64String(sent.ImageBase64).Length > 0);
    }

    [Fact]
    public async Task ModelAnswerOutsideTheSchema_IsNotAnError_ButUnreadable()
    {
        var (function, _) = Build("Xin lỗi, tôi không thấy ảnh.");
        var output = await function.HandleAsync(Input(), new TestLambdaContext());
        Assert.False(output.Readable);
        Assert.Null(output.Value);
    }

    [Fact]
    public async Task ModelFailure_PropagatesSoTheStateMachineCanRetry()
    {
        var store = new FakeObjectStore();
        store.Objects[(StorageBucket.Incoming, Key)] = (SampleImages.JpegWithGps(), "image/jpeg");
        var function = new ReadFunction(store, new FakeAi((_, _) => throw new TimeoutException("bedrock")));
        await Assert.ThrowsAsync<TimeoutException>(() => function.HandleAsync(Input(), new TestLambdaContext()));
    }

    [Fact]
    public async Task MissingPhoto_Throws()
    {
        var function = new ReadFunction(new FakeObjectStore(), new FakeAi((_, _) => "{}"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => function.HandleAsync(Input(), new TestLambdaContext()));
    }
}
