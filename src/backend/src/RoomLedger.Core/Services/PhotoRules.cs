using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Services;

/**
 * Quy tắc sau khi ảnh tới bucket incoming (BR-16 RULE 4–5).
 *
 * @author Sang
 * @version 1.0
 */
public static class PhotoRules
{
    /** Ngưỡng mặc định; trên AWS đọc từ SSM /roomledger/{stage}/photo/max-age-days (doc-fix-checklist B5). */
    public const int DefaultMaxAgeDays = 3;

    /**
     * Ảnh chụp cũ hơn maxAgeDays so với lúc nhận → cảnh báo (không chặn). Không có ngày chụp, hoặc ngày ở tương lai
     * (đồng hồ điện thoại sai) → không cảnh báo; nhãn nguồn ảnh GALLERY đã cho hai bên biết.
     *
     * @author Sang
     * @version 1.0
     */
    public static bool IsOldPhoto(DateTimeOffset? takenAt, DateTimeOffset receivedAt, int maxAgeDays) =>
        takenAt is { } taken && receivedAt - taken > TimeSpan.FromDays(maxAgeDays);

    /**
     * Biên bản còn nhận ảnh không (K2): nhận phòng khi OPEN hoặc AWAITING_CONFIRM; trả phòng chỉ khi OPEN (sau khi gửi
     * so sánh là COMPARING trở đi). Dùng ở photo-ingest để chặn ảnh có URL cấp trước lúc khóa nhưng tải lên sau.
     *
     * @author Sang
     * @version 1.0
     */
    public static bool AcceptsPhotos(InspectionType type, InspectionStatus status) => type == InspectionType.MoveIn
        ? status is InspectionStatus.Open or InspectionStatus.AwaitingConfirm
        : status == InspectionStatus.Open;

    /** Bản xem JPEG trong bucket derived: cùng đường dẫn với key incoming, đuôi .jpg. */
    public static string ViewKey(string incomingKey) => WithoutExtension(incomingKey) + ".jpg";

    /** Thumbnail 400 px trong bucket derived. */
    public static string ThumbnailKey(string incomingKey) => WithoutExtension(incomingKey) + ".thumb.jpg";

    /** Lấy photoId từ key {landlordId}/{tenancyId}/{inspectionId}/{photoId}.{ext}; key lạ → null. */
    public static Guid? PhotoIdFromKey(string key)
    {
        var parts = key.Split('/');
        if (parts.Length != 4)
        {
            return null;
        }
        var name = parts[3];
        var dot = name.LastIndexOf('.');
        return Guid.TryParse(dot > 0 ? name[..dot] : name, out var id) ? id : null;
    }

    private static string WithoutExtension(string key)
    {
        var slash = key.LastIndexOf('/');
        var dot = key.LastIndexOf('.');
        return dot > slash ? key[..dot] : key;
    }
}
