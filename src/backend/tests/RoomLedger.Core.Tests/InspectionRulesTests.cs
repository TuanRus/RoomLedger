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

    [Theory]
    [InlineData(InspectionStatus.Open)]
    [InlineData(InspectionStatus.AwaitingConfirm)]
    public void EnsureEditable_AllowsOpenAndAwaitingConfirm(InspectionStatus status)
        => InspectionStateMachine.EnsureEditable(status);

    [Theory]
    [InlineData(InspectionStatus.Locked)]
    [InlineData(InspectionStatus.Comparing)]
    [InlineData(InspectionStatus.Reviewing)]
    [InlineData(InspectionStatus.Settled)]
    public void EnsureEditable_AfterTheLock_IsRecordLocked(InspectionStatus status)
    {
        // BR-17 RULE 2 (kiểm thử bảo mật K1): khóa rồi thì không xóa hay thay ảnh được
        var ex = Assert.Throws<RecordLockedException>(() => InspectionStateMachine.EnsureEditable(status));
        Assert.Equal(ErrorCodes.RecordLocked, ex.ErrorCode);
    }

    private static readonly InspectionArea Bedroom = new(Guid.NewGuid(), "Phòng ngủ", 4, 1);
    private static readonly InspectionArea Kitchen = new(Guid.NewGuid(), "Bếp", 1, 2);

    private static IEnumerable<(Guid, FileStatus)> Photos(InspectionArea area, int count, FileStatus status = FileStatus.Ready) =>
        Enumerable.Repeat((area.RoomAreaId, status), count);

    [Fact]
    public void FindIncompleteAreas_EveryAreaHasEnoughReadyPhotos_IsEmpty()
        => Assert.Empty(InspectionService.FindIncompleteAreas([Bedroom, Kitchen], Photos(Bedroom, 4).Concat(Photos(Kitchen, 2))));

    [Fact]
    public void FindIncompleteAreas_TooFewPhotos_NamesTheAreaWithCount()
    {
        var missing = InspectionService.FindIncompleteAreas([Bedroom, Kitchen], Photos(Bedroom, 2).Concat(Photos(Kitchen, 1)));
        Assert.Equal(["Phòng ngủ (2/4 ảnh)"], missing);
    }

    [Fact]
    public void FindIncompleteAreas_PhotoStillProcessing_MakesTheAreaIncomplete()
    {
        // BR-18 RULE 1: mọi ảnh phải READY; ảnh đang xử lý không được tính và chặn xác nhận
        var photos = Photos(Bedroom, 4).Concat(Photos(Kitchen, 1)).Concat(Photos(Kitchen, 1, FileStatus.Received));
        Assert.Equal(["Bếp (ảnh đang xử lý)"], InspectionService.FindIncompleteAreas([Bedroom, Kitchen], photos));
    }

    [Fact]
    public void FindIncompleteAreas_SealedCountsAsReady()
        => Assert.Empty(InspectionService.FindIncompleteAreas([Kitchen], Photos(Kitchen, 1, FileStatus.Sealed)));

    [Theory]
    [InlineData(InspectionStatus.Open)]
    [InlineData(InspectionStatus.AwaitingConfirm)]
    public void EnsureCanConfirm_OpenOrAwaiting_IsAllowed(InspectionStatus status)
        => InspectionStateMachine.EnsureCanConfirm(status);

    [Theory]
    [InlineData(InspectionStatus.Locked)]
    [InlineData(InspectionStatus.Settled)]
    public void EnsureCanConfirm_AlreadyLocked_IsRecordLocked(InspectionStatus status)
        => Assert.Throws<RecordLockedException>(() => InspectionStateMachine.EnsureCanConfirm(status));

    [Fact]
    public void EnsureCanOpenMoveIn_SettledTenancy_IsInvalidState()
    {
        var ex = Assert.Throws<InvalidStateException>(() => InspectionStateMachine.EnsureCanOpenMoveIn(new Tenancy { Status = TenancyStatus.Settled }));
        Assert.Equal(ErrorCodes.InvalidState, ex.ErrorCode);
    }
}
