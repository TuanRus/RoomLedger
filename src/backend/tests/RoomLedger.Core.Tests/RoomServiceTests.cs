using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Test luật BR-05 đến BR-08.
 *
 * @author Tuấn
 * @version 1.0
 */
public class RoomServiceTests
{
    [Fact]
    public void ValidateRoom_AddsMeterAreaWhenMissing()
    {
        var areas = RoomService.ValidateRoom("P.204", 3_500_000, [new AreaInput("Phòng ngủ", 4)]);
        Assert.Equal(2, areas.Count);
        Assert.Equal(RoomService.MeterAreaName, areas[^1].Name);
    }

    [Fact]
    public void ValidateRoom_KeepsMeterAreaWhenPresent()
    {
        var areas = RoomService.ValidateRoom("P.204", 3_500_000, [new AreaInput("đồng hồ điện nước", 3)]);
        Assert.Single(areas);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(3_500_000, 7)]
    [InlineData(3_500_000, 0)]
    public void ValidateRoom_RejectsInvalidRentOrMinPhotos(long rent, int minPhotos)
    {
        var ex = Assert.Throws<ValidationException>(() => RoomService.ValidateRoom("P.1", rent, [new AreaInput("Bếp", minPhotos)]));
        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
    }

    [Fact]
    public void ValidateRoom_RejectsMoreThanTenAreasIncludingMeter()
    {
        var areas = Enumerable.Range(1, 10).Select(i => new AreaInput($"Khu {i}", 1)).ToList();
        var ex = Assert.Throws<ValidationException>(() => RoomService.ValidateRoom("P.1", 1, areas));
        Assert.Contains(ex.Errors, e => e.Field == "areas");
    }

    [Fact]
    public void ValidateProperty_ReportsEveryInvalidField()
    {
        var ex = Assert.Throws<ValidationException>(() => RoomService.ValidateProperty("", null, 0, -1, null));
        Assert.Equal(["name", "address", "electricityRate", "waterRate"], ex.Errors.Select(e => e.Field));
    }

    [Theory]
    [InlineData(null, false, RoomDisplayStatus.Vacant)]
    [InlineData(TenancyStatus.Draft, false, RoomDisplayStatus.Vacant)]
    [InlineData(TenancyStatus.AwaitingTenant, false, RoomDisplayStatus.AwaitingConfirm)]
    [InlineData(TenancyStatus.Active, true, RoomDisplayStatus.AwaitingConfirm)]
    [InlineData(TenancyStatus.Active, false, RoomDisplayStatus.Active)]
    [InlineData(TenancyStatus.MovingOut, false, RoomDisplayStatus.MovingOut)]
    public void DisplayStatus_FollowsBr08Rule2(TenancyStatus? tenancy, bool inspectionAwaiting, RoomDisplayStatus expected) =>
        Assert.Equal(expected, RoomService.DisplayStatus(tenancy, inspectionAwaiting));

    [Theory]
    [InlineData(TenancyStatus.Active, true)]
    [InlineData(TenancyStatus.MovingOut, true)]
    [InlineData(TenancyStatus.AwaitingTenant, false)]
    [InlineData(null, false)]
    public void AreasLocked_OnlyWhenOccupied(TenancyStatus? status, bool locked) =>
        Assert.Equal(locked, RoomService.AreasLocked(status));
}
