using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using RoomLedger.Api.Middleware;
using RoomLedger.Core.Common;

namespace RoomLedger.Api.Auth;

/**
 * Trả lỗi 401/403 theo định dạng chuẩn {success:false, message, errorCode, traceId}
 * thay vì body rỗng mặc định của ASP.NET Core.
 *
 * @author Tuấn
 * @version 1.0
 */
public class JsonAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /**
     * Ghi body lỗi khi bị chặn, còn lại để handler mặc định xử lý.
     *
     * @author Tuấn
     * @version 1.0
     */
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await ErrorHandlingMiddleware.WriteErrorAsync(context, 401, ErrorCodes.Unauthorized, "Bạn cần đăng nhập");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await ErrorHandlingMiddleware.WriteErrorAsync(context, 403, ErrorCodes.Forbidden, "Bạn không có quyền thực hiện thao tác này");
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
