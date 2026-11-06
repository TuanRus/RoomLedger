using Amazon.Lambda.Core;

namespace RoomLedger.Functions.ComparePairs;

/// <summary>Lọc perceptual hash, gọi Bedrock cho cặp khác biệt (ngoài VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("ComparePairs started");
        return Task.CompletedTask;
    }
}
