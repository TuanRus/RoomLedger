using Amazon.Lambda.Core;

namespace RoomLedger.Functions.PhotoDerive;

/// <summary>HEIC → JPEG, thumbnail, xóa GPS, ghi bucket derived (ngoài VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("PhotoDerive started");
        return Task.CompletedTask;
    }
}
