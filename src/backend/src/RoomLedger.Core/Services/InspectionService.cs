using System.Text.Json;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Services;

/**
 * Quy tắc thuần của biên bản nhận/trả phòng (MD-04, MD-06), không đụng database.
 * 1.1: kỳ trả phòng và chỉ số tháng cuối (MD-06-FE-05, quyết định A3, B1, B2).
 *
 * @author Sang
 * @version 1.1
 */
public static class InspectionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /**
     * Bản sao danh sách khu vực của phòng lúc mở biên bản, theo thứ tự hiển thị (BR-15 RULE 2).
     *
     * @author Sang
     * @version 1.0
     */
    public static IReadOnlyList<InspectionArea> BuildAreaSnapshot(IEnumerable<RoomArea> areas) =>
        areas.OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .Select(a => new InspectionArea(a.Id, a.Name, a.MinPhotos, a.SortOrder))
            .ToList();

    /** Ghi bản sao thành JSON (camelCase) cho cột inspection.area_snapshot_json. */
    public static string SerializeSnapshot(IReadOnlyList<InspectionArea> areas) => JsonSerializer.Serialize(areas, Json);

    /**
     * Khu vực chưa đủ điều kiện xác nhận (BR-18 RULE 1): ít ảnh READY hơn minPhotos, hoặc còn ảnh đang xử lý.
     * Trả mô tả theo thứ tự hiển thị, ví dụ "Phòng ngủ (2/4 ảnh)", "Bếp (ảnh đang xử lý)"; rỗng = đủ.
     *
     * @author Sang
     * @version 1.0
     */
    public static IReadOnlyList<string> FindIncompleteAreas(IReadOnlyList<InspectionArea> areas, IEnumerable<(Guid AreaId, FileStatus Status)> photos)
    {
        var byArea = photos.ToLookup(p => p.AreaId, p => p.Status);
        var missing = new List<string>();
        foreach (var area in areas.OrderBy(a => a.SortOrder))
        {
            var statuses = byArea[area.RoomAreaId].ToList();
            var ready = statuses.Count(s => s is FileStatus.Ready or FileStatus.Sealed);
            if (ready < area.MinPhotos)
            {
                missing.Add($"{area.Name} ({ready}/{area.MinPhotos} ảnh)");
            }
            else if (ready < statuses.Count)
            {
                missing.Add($"{area.Name} (ảnh đang xử lý)");
            }
        }
        return missing;
    }

    /** Đọc bản sao; rỗng hoặc null → không có khu vực. */
    public static IReadOnlyList<InspectionArea> ParseSnapshot(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<InspectionArea>>(json, Json) ?? [];

    /** Giờ Việt Nam (UTC+7, không có giờ mùa hè). */
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /**
     * Kỳ trả phòng (quyết định A3 của Tuấn, 17/11): tháng giờ Việt Nam của lúc mở biên bản trả phòng, dạng yyyy-MM.
     * Chốt lúc mở chứ không lúc gửi, để mở 30/11 rồi gửi 01/12 vẫn là kỳ 11.
     *
     * @author Sang
     * @version 1.0
     */
    public static string FinalPeriod(DateTimeOffset utcNow) =>
        utcNow.ToOffset(VietnamOffset).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    /**
     * Đồng hồ còn thiếu chỉ số tháng cuối (quyết định B1): một chỉ số được tính khi ảnh đồng hồ đã tới (khác
     * PENDING_UPLOAD), hoặc chỉ số đã qua bước PENDING_AI (đã có số, kể cả khi trạng thái ảnh chưa được cập nhật).
     * Không cần chủ nhà xác nhận. Trả theo thứ tự điện, nước; rỗng = đủ.
     *
     * @author Sang
     * @version 1.0
     */
    public static IReadOnlyList<MeterKind> MissingFinalReadings(
        IEnumerable<(MeterKind Kind, FileStatus? PhotoStatus, MeterReadingStatus Status)> readings)
    {
        var present = readings
            .Where(r => r.PhotoStatus is not (null or FileStatus.PendingUpload) || r.Status != MeterReadingStatus.PendingAi)
            .Select(r => r.Kind)
            .ToHashSet();
        return new[] { MeterKind.Electricity, MeterKind.Water }.Where(k => !present.Contains(k)).ToList();
    }

    /** Thông báo lỗi FINAL_READING_MISSING (quyết định B2), ví dụ "Thiếu ảnh đồng hồ: điện, nước (kỳ 2026-11)". */
    public static string FinalReadingMissingMessage(IReadOnlyList<MeterKind> missing, string period) =>
        $"Thiếu ảnh đồng hồ: {string.Join(", ", missing.Select(MeterName))} (kỳ {period})";

    /** Tên đồng hồ trong thông báo tiếng Việt. */
    public static string MeterName(MeterKind kind) => kind == MeterKind.Electricity ? "điện" : "nước";
}
