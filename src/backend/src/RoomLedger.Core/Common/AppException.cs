namespace RoomLedger.Core.Common;

/**
 * Lỗi nghiệp vụ có kiểu. ErrorHandlingMiddleware đổi thành mã HTTP và errorCode,
 * service không tự ghi response.
 *
 * @author Tuấn
 * @version 1.1
 */
public class AppException(int statusCode, string errorCode, string message, IReadOnlyList<object>? details = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public IReadOnlyList<object>? Details { get; } = details;
}

/**
 * Dữ liệu không hợp lệ (400 VALIDATION_ERROR), kèm danh sách lỗi theo từng trường.
 *
 * @author Tuấn
 * @version 1.0
 */
public class ValidationException(IReadOnlyList<FieldError> errors, string? message = null)
    : AppException(400, ErrorCodes.ValidationError, message ?? BuildMessage(errors), errors.Cast<object>().ToList())
{
    public IReadOnlyList<FieldError> Errors { get; } = errors;

    /**
     * Tạo lỗi cho một trường duy nhất.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static ValidationException For(string field, string message) => new([new FieldError(field, message)]);

    private static string BuildMessage(IReadOnlyList<FieldError> errors) =>
        errors.Count == 1 ? errors[0].Message : "Dữ liệu không hợp lệ";
}

/**
 * Lỗi 400 có errorCode riêng (INVALID_EMAIL, INVALID_FILE, INVALID_PERIOD).
 *
 * @author Tuấn
 * @version 1.0
 */
public class BadRequestException(string errorCode, string message) : AppException(400, errorCode, message);

/**
 * Bản ghi không tồn tại hoặc không thuộc về người gọi (404). Không dùng 403 để không lộ dữ liệu người khác.
 *
 * @author Tuấn
 * @version 1.0
 */
public class NotFoundException(string message = "Không tìm thấy dữ liệu") : AppException(404, ErrorCodes.NotFound, message);

/**
 * Sai vai hoặc không có quyền thao tác (403).
 *
 * @author Tuấn
 * @version 1.0
 */
public class ForbiddenException(string message = "Bạn không có quyền thực hiện thao tác này", string errorCode = ErrorCodes.Forbidden)
    : AppException(403, errorCode, message);

/**
 * Bản ghi đã khóa, không sửa hay xóa được (409 RECORD_LOCKED).
 *
 * @author Tuấn
 * @version 1.0
 */
public class RecordLockedException(string message = "Bản ghi đã khóa, không sửa được") : AppException(409, ErrorCodes.RecordLocked, message);

/**
 * Sai trạng thái cho thao tác (409 INVALID_STATE).
 *
 * @author Tuấn
 * @version 1.0
 */
public class InvalidStateException(string message = "Trạng thái hiện tại không cho phép thao tác này")
    : AppException(409, ErrorCodes.InvalidState, message);

/**
 * Xung đột dữ liệu 409 có errorCode riêng (ROOM_CODE_EXISTS, ROOM_OCCUPIED, PERIOD_CLOSED, VERSION_CONFLICT).
 *
 * @author Tuấn
 * @version 1.0
 */
public class ConflictException(string errorCode, string message, IReadOnlyList<object>? details = null)
    : AppException(409, errorCode, message, details);

/**
 * Link mời đã dùng, đã thu hồi hoặc hết hạn (410 INVITE_INVALID).
 *
 * @author Tuấn
 * @version 1.0
 */
public class InviteInvalidException()
    : AppException(410, ErrorCodes.InviteInvalid, "Link mời không còn hiệu lực, hãy nhờ chủ nhà gửi lại");
