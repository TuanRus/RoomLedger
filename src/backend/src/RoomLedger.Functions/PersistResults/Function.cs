using Amazon.Lambda.Core;

namespace RoomLedger.Functions.PersistResults;

/// <summary>Ghi kết quả Step Functions vào RDS (trong VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("PersistResults started");
        return Task.CompletedTask;
    }
}
