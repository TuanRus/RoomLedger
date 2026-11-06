using Amazon.Lambda.Core;

namespace RoomLedger.Functions.PhotoIngest;

/// <summary>Bucket incoming ObjectCreated: kiểm checksum, đọc EXIF, ghi bảng photo (trong VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("PhotoIngest started");
        return Task.CompletedTask;
    }
}
