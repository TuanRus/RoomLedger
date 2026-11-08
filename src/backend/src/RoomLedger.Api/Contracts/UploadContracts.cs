using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Body của POST /api/v1/uploads. Trường để nullable để kiểm thiếu trường và trả VALIDATION_ERROR theo từng trường.
 * sha256: SHA-256 của file gốc, mã hóa base64 (FE tính bằng WebCrypto).
 *
 * @author Sang
 * @version 1.0
 */
public record CreateUploadRequest(
    UploadTargetType? TargetType,
    Guid? TargetId,
    Guid? RoomAreaId,
    string? FileName,
    string? ContentType,
    long? Size,
    string? Sha256,
    CaptureSource? CaptureSource);

/**
 * Presigned PUT: FE gửi đúng method và headers (gồm x-amz-checksum-sha256) trước expiresAt.
 *
 * @author Sang
 * @version 1.0
 */
public record UploadResponse(
    Guid PhotoId,
    string UploadUrl,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt);
