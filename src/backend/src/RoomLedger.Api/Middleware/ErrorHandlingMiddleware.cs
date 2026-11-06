using RoomLedger.Core.Common;

namespace RoomLedger.Api.Middleware;

/// <summary>Đổi AppException thành {success:false, message, errorCode, traceId}.</summary>
public class ErrorHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new ApiError(ex.Message, ex.ErrorCode, context.TraceIdentifier));
        }
    }
}
