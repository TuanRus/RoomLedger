using System.Text.Json;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Services;

/**
 * Quy tắc thuần của biên bản nhận/trả phòng (MD-04, MD-06), không đụng database.
 *
 * @author Sang
 * @version 1.0
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
}
