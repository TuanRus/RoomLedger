using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Microsoft.Extensions.Time.Testing;
using RoomLedger.Core.Abstractions;
using RoomLedger.Infrastructure.Bedrock;
using RoomLedger.Infrastructure.Config;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * BedrockAiClient (Converse) và SsmConfigProvider (bộ nhớ đệm 5 phút), dùng AWS SDK giả.
 *
 * @author Tuấn
 * @version 1.0
 */
public class BedrockAiClientTests
{
    private sealed class MapConfig(Dictionary<string, string> values) : IConfigProvider
    {
        public Task<string> GetAsync(string name, CancellationToken ct) => Task.FromResult(values[name]);
    }

    private static string Input(string base64 = "AQID") => $$"""{"imageBase64":"{{base64}}","mediaType":"image/jpeg"}""";

    [Fact]
    public async Task Invoke_SendsModelPromptSchemaAndImage_AndReturnsTheModelText()
    {
        var bedrock = FakeAws<IAmazonBedrockRuntime>.Create((_, _) => new ConverseResponse
        {
            Output = new ConverseOutput { Message = new Message { Role = ConversationRole.Assistant, Content = [new ContentBlock { Text = "{\"readable\":true}" }] } },
        }, out var fake);
        var config = new MapConfig(new()
        {
            ["ai/model-id"] = " au.anthropic.claude-haiku-4-5-20251001-v1:0 \n",
            ["ai/prompt/readWaterMeter"] = "PROMPT-NUOC",
        });
        var client = new BedrockAiClient(bedrock, config, task => $"SCHEMA-{task}");

        var text = await client.InvokeAsync("readWaterMeter", Input(), CancellationToken.None);

        Assert.Equal("{\"readable\":true}", text);
        var request = Assert.IsType<ConverseRequest>(Assert.Single(fake.Calls).Request);
        Assert.Equal("au.anthropic.claude-haiku-4-5-20251001-v1:0", request.ModelId);
        var system = Assert.Single(request.System).Text;
        Assert.StartsWith("PROMPT-NUOC", system);
        Assert.Contains("SCHEMA-readWaterMeter", system);
        var content = Assert.Single(request.Messages).Content;
        Assert.Equal(ImageFormat.Jpeg, content[0].Image.Format);
        Assert.Equal(new byte[] { 1, 2, 3 }, ((MemoryStream)content[0].Image.Source.Bytes).ToArray());
        Assert.Equal(0f, request.InferenceConfig.Temperature);
        Assert.Equal(BedrockAiClient.MaxOutputTokens, request.InferenceConfig.MaxTokens);
    }

    [Fact]
    public async Task Invoke_BedrockFails_ThrowsSoTheCallerCanRetry()
    {
        var bedrock = FakeAws<IAmazonBedrockRuntime>.Create((_, _) => throw new ThrottlingException("slow down"), out _);
        var client = new BedrockAiClient(bedrock, new MapConfig(new() { ["ai/model-id"] = "m", ["ai/prompt/t"] = "p" }), _ => "{}");
        await Assert.ThrowsAsync<ThrottlingException>(() => client.InvokeAsync("t", Input(), CancellationToken.None));
    }

    [Fact]
    public async Task Invoke_UnsupportedImageType_IsRejectedBeforeCallingBedrock()
    {
        var bedrock = FakeAws<IAmazonBedrockRuntime>.Create((_, _) => throw new InvalidOperationException("không được gọi"), out var fake);
        var client = new BedrockAiClient(bedrock, new MapConfig(new() { ["ai/model-id"] = "m", ["ai/prompt/t"] = "p" }), _ => "{}");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.InvokeAsync("t", """{"imageBase64":"AQID","mediaType":"image/heic"}""", CancellationToken.None));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task SsmConfig_ReadsUnderThePrefix_AndCachesForFiveMinutes()
    {
        var ssm = FakeAws<IAmazonSimpleSystemsManagement>.Create(
            (_, _) => new GetParameterResponse { Parameter = new Parameter { Value = "v" } }, out var fake);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-27T00:00:00Z"));
        var provider = new SsmConfigProvider(ssm, "/roomledger/dev/", clock);

        Assert.Equal("v", await provider.GetAsync("ai/model-id", CancellationToken.None));
        Assert.Equal("v", await provider.GetAsync("ai/model-id", CancellationToken.None));
        Assert.Single(fake.Calls);
        Assert.Equal("/roomledger/dev/ai/model-id", ((GetParameterRequest)fake.Calls[0].Request!).Name);

        clock.Advance(SsmConfigProvider.CacheFor + TimeSpan.FromSeconds(1));
        await provider.GetAsync("ai/model-id", CancellationToken.None);
        Assert.Equal(2, fake.Calls.Count);
    }
}
