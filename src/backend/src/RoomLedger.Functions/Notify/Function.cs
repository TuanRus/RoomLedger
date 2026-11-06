using Amazon.Lambda.Core;

namespace RoomLedger.Functions.Notify;

/// <summary>Gửi email qua SES; lỗi vào SQS DLQ (ngoài VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("Notify started");
        return Task.CompletedTask;
    }
}
