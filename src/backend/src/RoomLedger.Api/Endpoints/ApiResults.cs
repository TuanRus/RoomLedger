using RoomLedger.Core.Common;

namespace RoomLedger.Api.Endpoints;

/**
 * Bọc dữ liệu vào định dạng {success, message, data} cho mọi endpoint.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class ApiResults
{
    /**
     * 200 OK kèm dữ liệu.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IResult Ok<T>(T data, string message = "OK") => Results.Ok(ApiResponse<T>.Ok(data, message));

    /**
     * 201 Created kèm dữ liệu.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IResult Created<T>(T data, string message) =>
        Results.Json(ApiResponse<T>.Ok(data, message), statusCode: StatusCodes.Status201Created);
}
