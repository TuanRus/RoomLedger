using RoomLedger.Core.Common;

namespace RoomLedger.Core.Services;

/**
 * Quy tắc tải ảnh lên (BR-16 RULE 1–2): JPEG, PNG hoặc HEIC; tối đa 25 MB; tối đa 20 ảnh mỗi khu vực;
 * SHA-256 dạng base64 (32 byte) để S3 tự kiểm toàn vẹn; presigned URL có hạn 5 phút.
 * Ảnh được gửi nguyên bản, không nén hay resize.
 *
 * @author Sang
 * @version 1.0
 */
public static class UploadRules
{
    public const int MaxPhotosPerArea = 20;
    public const long MaxPhotoBytes = 25L * 1024 * 1024;
    public static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(5);

    private static readonly Dictionary<string, string> PhotoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/heic"] = "heic",
    };

    /**
     * Kiểm định dạng, dung lượng và SHA-256 của ảnh; trả về phần mở rộng dùng trong key S3.
     * Sai → 400 INVALID_FILE (BR-16 EXCEPTION 1).
     *
     * @author Sang
     * @version 1.0
     */
    public static string ValidatePhoto(string contentType, long size, string sha256Base64)
    {
        if (!PhotoTypes.TryGetValue(contentType ?? "", out var ext))
        {
            throw new BadRequestException(ErrorCodes.InvalidFile, "Chỉ nhận ảnh JPEG, PNG hoặc HEIC");
        }
        if (size <= 0 || size > MaxPhotoBytes)
        {
            throw new BadRequestException(ErrorCodes.InvalidFile, "Ảnh phải lớn hơn 0 và không quá 25 MB");
        }
        if (!IsSha256Base64(sha256Base64))
        {
            throw new BadRequestException(ErrorCodes.InvalidFile, "SHA-256 phải là 32 byte mã hóa base64");
        }
        return ext;
    }

    /** Key trong bucket incoming: {landlordId}/{tenancyId}/{inspectionId}/{photoId}.{ext} (BR-16 RULE 2). */
    public static string BuildIncomingKey(Guid landlordId, Guid tenancyId, Guid inspectionId, Guid photoId, string ext) =>
        $"{landlordId}/{tenancyId}/{inspectionId}/{photoId}.{ext}";

    private static bool IsSha256Base64(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 44)
        {
            return false;
        }
        Span<byte> buffer = stackalloc byte[33];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 32;
    }
}
