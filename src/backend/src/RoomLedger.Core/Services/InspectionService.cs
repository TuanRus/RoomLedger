using System.Text.Json;
using RoomLedger.Core.Domain;

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

    /** Đọc bản sao; rỗng hoặc null → không có khu vực. */
    public static IReadOnlyList<InspectionArea> ParseSnapshot(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<InspectionArea>>(json, Json) ?? [];
}
