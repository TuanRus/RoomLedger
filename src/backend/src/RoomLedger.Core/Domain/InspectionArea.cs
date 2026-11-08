namespace RoomLedger.Core.Domain;

/**
 * Một khu vực trong bản sao danh sách khu vực của biên bản (inspection.area_snapshot, jsonb), chụp lúc mở biên bản
 * nhận phòng (BR-15 RULE 2). Biên bản trả phòng dùng lại đúng danh sách này (BR-25 RULE 1).
 *
 * @author Sang
 * @version 1.0
 */
public sealed record InspectionArea(Guid RoomAreaId, string Name, int MinPhotos, int SortOrder);
