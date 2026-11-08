using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Body POST /uploads. sha256 là SHA-256 của file gốc, mã hóa base64 (44 ký tự).
 *
 * @author Tuấn
 * @version 1.0
 */
public record CreateUploadRequest(
    UploadTargetType? TargetType,
    Guid? TargetId,
    Guid? AreaId,
    string? FileName,
    string? ContentType,
    long? Size,
    string? Sha256);

/**
 * Presigned PUT URL và các header FE phải gửi đúng khi PUT lên S3 (hạn 5 phút).
 *
 * @author Tuấn
 * @version 1.0
 */
public record UploadUrlResponse(
    Guid FileId,
    string UploadUrl,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);
