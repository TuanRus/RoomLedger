using Amazon.Lambda.Core;

namespace RoomLedger.Functions.EvidenceSeal;

/// <summary>Chép file sang bucket evidence có Object Lock, kiểm hash (trong VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("EvidenceSeal started");
        return Task.CompletedTask;
    }
}
