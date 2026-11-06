using Amazon.Lambda.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomLedger.Core.Common;

namespace RoomLedger.Api.Middleware;

/**
 * Bắt mọi lỗi và trả JSON thống nhất {success:false, message, errorCode, traceId, details}.
 * Không trả stack trace hay trang lỗi mặc định. Log không chứa email, số điện thoại hay GPS.
 *
 * @author Tuấn
 * @version 1.1
 */
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    /**
     * Chạy request và đổi exception thành mã HTTP + errorCode.
     *
     * @author Tuấn
     * @version 1.1
     */
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            await WriteErrorAsync(context, ex.StatusCode, ex.ErrorCode, ex.Message, ex.Details);
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteErrorAsync(context, 409, ErrorCodes.VersionConflict, "Dữ liệu đã thay đổi, vui lòng tải lại");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await WriteErrorAsync(context, 409, ErrorCodes.InvalidState, "Dữ liệu đã tồn tại hoặc vừa được người khác tạo, vui lòng tải lại");
        }
        catch (BadHttpRequestException)
        {
            await WriteErrorAsync(context, 400, ErrorCodes.ValidationError, "Dữ liệu gửi lên không đúng định dạng");
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            // Chỉ log loại lỗi và traceId; message của exception có thể chứa dữ liệu người dùng
            logger.LogError("Unhandled {ExceptionType} at {Path} traceId={TraceId}\n{StackTrace}",
                ex.GetType().Name, context.Request.Path, GetTraceId(context), ex.StackTrace);
            await WriteErrorAsync(context, 500, ErrorCodes.InternalError, "Có lỗi, vui lòng thử lại");
        }
    }

    /**
     * Ghi một lỗi theo định dạng chuẩn. Dùng chung cho middleware và handler phân quyền.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static Task WriteErrorAsync(HttpContext context, int status, string errorCode, string message, IReadOnlyList<object>? details = null)
    {
        context.Response.Clear();
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ApiError(message, errorCode, GetTraceId(context), details));
    }

    /**
     * traceId để tra log CloudWatch: Root của X-Ray, sau đó là request id của Lambda, cuối cùng là TraceIdentifier.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static string GetTraceId(HttpContext context)
    {
        var xray = context.Request.Headers["X-Amzn-Trace-Id"].ToString();
        var root = xray.Split(';').FirstOrDefault(p => p.StartsWith("Root=", StringComparison.Ordinal));
        if (root is not null)
        {
            return root[5..];
        }

        if (context.Items.TryGetValue("LambdaContext", out var lc) && lc is ILambdaContext lambda)
        {
            return lambda.AwsRequestId;
        }

        return context.TraceIdentifier;
    }
}
