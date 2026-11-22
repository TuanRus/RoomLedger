using Amazon.Lambda.Core;

namespace RoomLedger.Functions.ReadDocument;

/// <summary>AI đọc đồng hồ điện nước (ngoài VPC). Hợp đồng không dùng AI.</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("ReadDocument started");
        return Task.CompletedTask;
    }
}
