using Amazon.Lambda.Core;

namespace RoomLedger.Functions.Migrate;

/// <summary>Chạy SQL migration EF Core idempotent bằng roomledger_migrator (trong VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("Migrate started");
        return Task.CompletedTask;
    }
}
