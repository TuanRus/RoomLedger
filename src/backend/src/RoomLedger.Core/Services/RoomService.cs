using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Services;

/**
 * Một khu vực cần chụp khi tạo hoặc sửa phòng.
 *
 * @author Tuấn
 * @version 1.0
 */
public record AreaInput(string Name, int MinPhotos);

/**
 * Luật nghiệp vụ của nhà trọ và phòng (BR-05 đến BR-08), không phụ thuộc database.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class RoomService
{
    public const string MeterAreaName = "Đồng hồ điện nước";
    public const int MaxAreas = 10;

    /**
     * Kiểm dữ liệu tạo nhà trọ (BR-05 RULE 1): tên 1–100 ký tự, địa chỉ bắt buộc, giá điện nước > 0.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void ValidateProperty(string? name, string? address, long? electricityRate, long? waterRate, long? serviceFee)
    {
        new Validator()
            .Text(name, "name", 1, 100, "Tên nhà trọ")
            .Text(address, "address", 1, 255, "Địa chỉ")
            .Positive(electricityRate, "electricityRate", "Giá điện")
            .Positive(waterRate, "waterRate", "Giá nước")
            .Require(serviceFee is null or >= 0, "serviceFee", "Phí dịch vụ không được âm")
            .ThrowIfInvalid();
    }

    /**
     * Kiểm dữ liệu phòng (BR-06 RULE 2) và trả danh sách khu vực đã chuẩn hóa,
     * tự thêm khu vực "Đồng hồ điện nước" nếu chủ nhà chưa có.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IReadOnlyList<AreaInput> ValidateRoom(string? code, long? monthlyRent, IReadOnlyList<AreaInput>? areas)
    {
        var v = new Validator()
            .Text(code, "code", 1, 20, "Mã phòng")
            .Positive(monthlyRent, "monthlyRent", "Giá thuê");
        var normalized = ValidateAreas(v, areas);
        v.ThrowIfInvalid();
        return normalized;
    }

    /**
     * Kiểm danh sách khu vực khi sửa phòng (dùng chung luật với tạo phòng).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static IReadOnlyList<AreaInput> ValidateAreasOnly(IReadOnlyList<AreaInput>? areas)
    {
        var v = new Validator();
        var normalized = ValidateAreas(v, areas);
        v.ThrowIfInvalid();
        return normalized;
    }

    private static List<AreaInput> ValidateAreas(Validator v, IReadOnlyList<AreaInput>? areas)
    {
        var list = (areas ?? [])
            .Select(a => new AreaInput(a.Name?.Trim() ?? "", a.MinPhotos))
            .ToList();
        for (var i = 0; i < list.Count; i++)
        {
            v.Text(list[i].Name, $"areas[{i}].name", 1, 50, "Tên khu vực");
            v.Require(list[i].MinPhotos is >= 1 and <= 6, $"areas[{i}].minPhotos", "Số ảnh tối thiểu phải từ 1 đến 6");
        }

        v.Require(list.Select(a => a.Name.ToLowerInvariant()).Distinct().Count() == list.Count, "areas", "Tên khu vực bị trùng");

        if (!list.Any(a => string.Equals(a.Name, MeterAreaName, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(new AreaInput(MeterAreaName, 2));
        }

        v.Require(list.Count is >= 1 and <= MaxAreas, "areas", $"Phòng phải có từ 1 đến {MaxAreas} khu vực (kể cả khu vực đồng hồ)");
        return list;
    }

    /**
     * Không cho sửa khu vực khi phòng có hợp đồng ACTIVE hoặc MOVING_OUT (BR-07 RULE 2),
     * để biên bản trả phòng khớp biên bản nhận phòng.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static bool AreasLocked(TenancyStatus? currentTenancyStatus) =>
        currentTenancyStatus is TenancyStatus.Active or TenancyStatus.MovingOut;

    /**
     * Tính trạng thái hiển thị của phòng (BR-08 RULE 2).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static RoomDisplayStatus DisplayStatus(TenancyStatus? currentTenancyStatus, bool inspectionAwaitingConfirm) =>
        currentTenancyStatus switch
        {
            TenancyStatus.AwaitingTenant => RoomDisplayStatus.AwaitingConfirm,
            TenancyStatus.Active when inspectionAwaitingConfirm => RoomDisplayStatus.AwaitingConfirm,
            TenancyStatus.Active => RoomDisplayStatus.Active,
            TenancyStatus.MovingOut => RoomDisplayStatus.MovingOut,
            _ => RoomDisplayStatus.Vacant,
        };
}
