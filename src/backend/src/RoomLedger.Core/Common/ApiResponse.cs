namespace RoomLedger.Core.Common;

/**
 * Định dạng phản hồi thành công chung cho mọi API: {success, message, data}.
 *
 * @author Tuấn
 * @version 1.1
 */
public record ApiResponse<T>(bool Success, string Message, T? Data)
{
    /**
     * Tạo phản hồi thành công kèm dữ liệu.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static ApiResponse<T> Ok(T data, string message = "OK") => new(true, message, data);
}

/**
 * Định dạng lỗi chung: {success:false, message, errorCode, traceId, details}.
 * details chỉ có khi VALIDATION_ERROR hoặc lỗi cần chỉ ra phần bị thiếu.
 *
 * @author Tuấn
 * @version 1.1
 */
public record ApiError(string Message, string ErrorCode, string TraceId, IReadOnlyList<object>? Details = null)
{
    public bool Success => false;
}

/**
 * Lỗi của một trường dữ liệu, dùng trong details của VALIDATION_ERROR.
 *
 * @author Tuấn
 * @version 1.0
 */
public record FieldError(string Field, string Message);

/**
 * Trang dữ liệu phân trang keyset: items và nextCursor (null khi hết).
 *
 * @author Tuấn
 * @version 1.0
 */
public record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);
