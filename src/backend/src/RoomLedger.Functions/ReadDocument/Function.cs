using Amazon.Lambda.Core;

namespace RoomLedger.Functions.ReadDocument;

/// <summary>Đọc đồng hồ điện nước và điều khoản hợp đồng (ngoài VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("ReadDocument started");
        return Task.CompletedTask;
    }
}
