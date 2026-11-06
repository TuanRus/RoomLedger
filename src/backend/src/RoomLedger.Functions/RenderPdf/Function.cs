using Amazon.Lambda.Core;

namespace RoomLedger.Functions.RenderPdf;

/// <summary>QuestPDF: biên bản nhận phòng, thanh lý (ngoài VPC)</summary>
public class Function
{
    public Task HandleAsync(object input, ILambdaContext context)
    {
        context.Logger.LogInformation("RenderPdf started");
        return Task.CompletedTask;
    }
}
