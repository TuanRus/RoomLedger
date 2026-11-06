namespace RoomLedger.Core.Common;

/// <summary>Định dạng phản hồi chung: {success, message, data}.</summary>
public record ApiResponse<T>(bool Success, string Message, T? Data);

/// <summary>Định dạng lỗi chung: {success:false, message, errorCode, traceId}.</summary>
public record ApiError(string Message, string ErrorCode, string TraceId)
{
    public bool Success => false;
}
