using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * Bedrock giả kế thừa client của SDK, không gọi mạng: mỗi model id trả một câu trả lời hoặc ném lỗi; ghi lại mọi request.
 * Dùng chung cho RoomPhotoComparerTests và ComparePairsTests.
 *
 * @author Sang
 * @version 1.0
 */
internal sealed class FakeBedrock() : AmazonBedrockRuntimeClient(new AnonymousAWSCredentials(), RegionEndpoint.APSoutheast2)
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
