using System.Text.Json;
using RoomLedger.Ai;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * Đọc kết quả AI (MeterAiResult), prompt và schema nhúng sẵn.
 *
 * @author Tuấn
 * @version 1.0
 */
public class MeterAiResultTests
{
    [Fact]
    public void Parse_CleanJson_GivesValueAndConfidence()
    {
        var r = MeterAiResult.Parse("""{"readable":true,"value":12345,"confidence":0.93,"note":""}""");
        Assert.Equal(new MeterAiResult(true, 12345, 0.93m, ""), r);
    }

    [Fact]
    public void Parse_JsonInsideMarkdownFenceAndChatter_IsStillRead()
    {
        var r = MeterAiResult.Parse("Đây là kết quả:\n```json\n{\"readable\":true,\"value\":7,\"confidence\":0.9,\"note\":\"\"}\n```");
        Assert.Equal(7, r!.Value);
    }

    [Fact]
    public void Parse_NotReadable_DropsTheValue()
    {
        var r = MeterAiResult.Parse("""{"readable":false,"value":99,"confidence":0.2,"note":"Ảnh mờ"}""");
        Assert.Equal(new MeterAiResult(false, null, 0.2m, "Ảnh mờ"), r);
    }

    [Theory]
    [InlineData("""{"readable":true,"value":-5,"confidence":0.9,"note":""}""")]
    [InlineData("""{"readable":true,"value":12.5,"confidence":0.9,"note":""}""")]
    [InlineData("""{"readable":true,"value":"123","confidence":0.9,"note":""}""")]
    [InlineData("""{"readable":true,"value":null,"confidence":0.9,"note":""}""")]
    public void Parse_ValueNotAWholeNonNegativeNumber_IsNotReadable(string json)
    {
        var r = MeterAiResult.Parse(json);
        Assert.False(r!.Readable);
        Assert.Null(r.Value);
    }

    [Fact]
    public void Parse_ConfidenceOutOfRange_IsClamped_AndMissingIsZero()
    {
        Assert.Equal(1m, MeterAiResult.Parse("""{"readable":true,"value":1,"confidence":7,"note":""}""")!.Confidence);
        Assert.Equal(0m, MeterAiResult.Parse("""{"readable":true,"value":1}""")!.Confidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("không có JSON")]
    [InlineData("{ hỏng")]
    [InlineData("{\"readable\": }")]
    public void Parse_NoUsableJson_ReturnsNull(string? text) => Assert.Null(MeterAiResult.Parse(text));

    [Theory]
    [InlineData(MeterKind.Electricity, AiTasks.ReadElectricMeter)]
    [InlineData(MeterKind.Water, AiTasks.ReadWaterMeter)]
    public void EmbeddedPromptAndSchema_Exist_AndSchemaIsValidJson(MeterKind kind, string expectedTask)
    {
        var task = AiTasks.For(kind);
        Assert.Equal(expectedTask, task);
        Assert.DoesNotContain("TODO", AiTasks.Prompt(task));
        using var schema = JsonDocument.Parse(AiTasks.Schema(task));
        Assert.Contains("value", schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }
}
