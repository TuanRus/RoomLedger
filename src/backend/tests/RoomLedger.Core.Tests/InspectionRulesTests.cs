using RoomLedger.Core.Common;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Core.StateMachines;

namespace RoomLedger.Core.Tests;

/**
 * Mở biên bản nhận phòng (BR-15): bản sao danh sách khu vực lúc mở, và hợp đồng đã quyết toán thì không mở được.
 *
 * @author Sang
 * @version 1.0
 */
public class InspectionRulesTests
{
    private static RoomArea Area(string name, int minPhotos, int sortOrder) =>
        new() { RoomId = Guid.NewGuid(), Name = name, MinPhotos = minPhotos, SortOrder = sortOrder };

    [Fact]
    public void BuildAreaSnapshot_KeepsIdNameMinPhotos_InSortOrder()
    {
        var meter = Area("Đồng hồ điện nước", 1, 2);
        var bedroom = Area("Phòng ngủ", 4, 1);

        var snapshot = InspectionService.BuildAreaSnapshot([meter, bedroom]);

        Assert.Equal([bedroom.Id, meter.Id], snapshot.Select(a => a.RoomAreaId));
        Assert.Equal(new InspectionArea(bedroom.Id, "Phòng ngủ", 4, 1), snapshot[0]);
    }

    [Fact]
    public void Snapshot_SurvivesJsonRoundTrip()
    {
        var snapshot = InspectionService.BuildAreaSnapshot([Area("Phòng ngủ", 4, 1), Area("Bếp", 2, 2)]);
        var json = InspectionService.SerializeSnapshot(snapshot);

        Assert.Equal(snapshot, InspectionService.ParseSnapshot(json));
        Assert.Contains("\"roomAreaId\"", json);   // camelCase trong jsonb, giống API
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseSnapshot_Empty_ReturnsNoAreas(string? json)
        => Assert.Empty(InspectionService.ParseSnapshot(json));

    [Theory]
    [InlineData(TenancyStatus.Draft)]
    [InlineData(TenancyStatus.AwaitingTenant)]
    [InlineData(TenancyStatus.Active)]
    [InlineData(TenancyStatus.MovingOut)]
    public void EnsureCanOpenMoveIn_AllowsEveryStatusExceptSettled(TenancyStatus status)
        => InspectionStateMachine.EnsureCanOpenMoveIn(new Tenancy { Status = status });

    [Fact]
    public void EnsureCanOpenMoveIn_SettledTenancy_IsInvalidState()
    {
        var ex = Assert.Throws<InvalidStateException>(() => InspectionStateMachine.EnsureCanOpenMoveIn(new Tenancy { Status = TenancyStatus.Settled }));
        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    }
}
