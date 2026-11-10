using RoomLedger.Core.Domain;
using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Lập kế hoạch so ảnh trả phòng (BR-26 RULE 1–2, EXCEPTION 1): ghép mỗi ảnh trả phòng với ảnh nhận phòng cùng khu vực
 * có khoảng cách perceptual hash nhỏ nhất; dưới ngưỡng → SKIPPED; còn lại gọi Bedrock tới giới hạn số lần gọi
 * (chia vòng tròn giữa các khu vực), vượt giới hạn → NOT_ANALYZED.
 *
 * @author Sang
 * @version 1.0
 */
public class ComparePlannerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly InspectionArea Bedroom = new(Guid.NewGuid(), "Phòng ngủ", 1, 0);
    private static readonly InspectionArea Bath = new(Guid.NewGuid(), "Nhà tắm", 1, 1);
    private static readonly InspectionArea[] Areas = [Bath, Bedroom];   // cố ý sai thứ tự: planner tự sắp theo SortOrder

    private static PlannedPhoto Photo(InspectionArea area, ulong? hash, int minute = 0) =>
        new(Guid.NewGuid(), area.RoomAreaId, hash, T0.AddMinutes(minute));

    private static IReadOnlyList<PlannedPair> Plan(IEnumerable<PlannedPhoto> moveIn, IEnumerable<PlannedPhoto> moveOut,
        int threshold = 6, int maxCalls = 30) =>
        ComparePlanner.Plan(Areas, moveIn.ToList(), moveOut.ToList(), threshold, maxCalls);

    [Fact]
    public void EachMoveOutPhoto_IsPairedWithTheClosestMoveInPhoto_OfTheSameArea()
    {
        var far = Photo(Bedroom, 0b1111_1111_1111);
        var near = Photo(Bedroom, 0b0000_0000_0000_0011_1111_1111, minute: 1);
        var otherArea = Photo(Bath, 0);                       // gần nhất nhưng khác khu vực → không được chọn
        var moveOut = Photo(Bedroom, 0);

        var pair = Assert.Single(Plan([far, near, otherArea], [moveOut]));

        Assert.Equal(moveOut.PhotoId, pair.MoveOutPhotoId);
        Assert.Equal(near.PhotoId, pair.MoveInPhotoId);
        Assert.Equal(Bedroom.RoomAreaId, pair.AreaId);
        Assert.Equal(10, pair.Distance);
        Assert.Equal(PairDecision.Compare, pair.Decision);
    }

    [Fact]
    public void BelowTheThreshold_IsSkipped_AtTheThreshold_IsCompared()
    {
        var moveIn = Photo(Bedroom, 0);
        var five = Photo(Bedroom, 0b1_1111);
        var six = Photo(Bedroom, 0b11_1111, minute: 1);

        var plan = Plan([moveIn], [five, six]);

        Assert.Equal(PairDecision.Skipped, plan.Single(p => p.MoveOutPhotoId == five.PhotoId).Decision);
        Assert.Equal(PairDecision.Compare, plan.Single(p => p.MoveOutPhotoId == six.PhotoId).Decision);
    }

    [Fact]
    public void EqualDistance_PicksTheEarlierUploadedMoveInPhoto()
    {
        var later = Photo(Bedroom, 0b01, minute: 5);
        var earlier = Photo(Bedroom, 0b10, minute: 1);

        var pair = Assert.Single(Plan([later, earlier], [Photo(Bedroom, 0)]));

        Assert.Equal(earlier.PhotoId, pair.MoveInPhotoId);
    }

    [Fact]
    public void NoMoveInPhotoInTheArea_IsNotAnalyzed()
    {
        var pair = Assert.Single(Plan([Photo(Bedroom, 0)], [Photo(Bath, 0)]));

        Assert.Null(pair.MoveInPhotoId);
        Assert.Equal(PairDecision.NotAnalyzed, pair.Decision);
        Assert.Equal("NO_MOVE_IN_PHOTO", pair.Reason);
    }

    [Fact]
    public void UnreadableMoveOutPhoto_CannotBeFiltered_SoItIsCompared_WithTheFirstMoveInPhoto()
    {
        var first = Photo(Bedroom, 0, minute: 0);
        var second = Photo(Bedroom, 0, minute: 1);

        var pair = Assert.Single(Plan([second, first], [Photo(Bedroom, null)]));

        Assert.Equal(first.PhotoId, pair.MoveInPhotoId);
        Assert.Null(pair.Distance);
        Assert.Equal(PairDecision.Compare, pair.Decision);
    }

    [Fact]
    public void UnreadableMoveInPhotos_AreOnlyUsedWhenNoReadableOneExists()
    {
        var unreadable = Photo(Bedroom, null, minute: 0);
        var readable = Photo(Bedroom, ulong.MaxValue, minute: 1);

        var pair = Assert.Single(Plan([unreadable, readable], [Photo(Bedroom, 0)]));

        Assert.Equal(readable.PhotoId, pair.MoveInPhotoId);
        Assert.Equal(64, pair.Distance);
    }

    [Fact]
    public void CallLimit_IsSharedRoundRobinAcrossAreas_TheRestAreNotAnalyzed()
    {
        PlannedPhoto[] moveIn = [Photo(Bedroom, 0), Photo(Bath, 0)];
        var bedroom = Enumerable.Range(0, 3).Select(i => Photo(Bedroom, ulong.MaxValue, i)).ToList();
        var bath = Enumerable.Range(0, 3).Select(i => Photo(Bath, ulong.MaxValue, i)).ToList();

        var plan = Plan(moveIn, bedroom.Concat(bath), maxCalls: 4);

        int Compared(IEnumerable<PlannedPhoto> photos) =>
            plan.Count(p => photos.Any(x => x.PhotoId == p.MoveOutPhotoId) && p.Decision == PairDecision.Compare);
        Assert.Equal(2, Compared(bedroom));
        Assert.Equal(2, Compared(bath));
        Assert.Equal(2, plan.Count(p => p.Decision == PairDecision.NotAnalyzed && p.Reason == "CALL_LIMIT"));
        Assert.Equal(bedroom[2].PhotoId, plan.Single(p => p.Reason == "CALL_LIMIT" && bedroom.Any(b => b.PhotoId == p.MoveOutPhotoId)).MoveOutPhotoId);
    }

    [Fact]
    public void SkippedPairs_DoNotUseUpTheCallLimit()
    {
        var moveIn = Photo(Bedroom, 0);
        var same = Enumerable.Range(0, 5).Select(i => Photo(Bedroom, 0, i));
        var different = Photo(Bedroom, ulong.MaxValue, 10);

        var plan = Plan([moveIn], same.Append(different), maxCalls: 1);

        Assert.Equal(5, plan.Count(p => p.Decision == PairDecision.Skipped));
        Assert.Equal(different.PhotoId, plan.Single(p => p.Decision == PairDecision.Compare).MoveOutPhotoId);
    }

    [Fact]
    public void ResultIsInAreaOrder_ThenUploadOrder_OnePairPerMoveOutPhoto()
    {
        PlannedPhoto[] moveIn = [Photo(Bedroom, 0), Photo(Bath, 0)];
        var bathLate = Photo(Bath, 0, 9);
        var bedroomLate = Photo(Bedroom, 0, 5);
        var bedroomEarly = Photo(Bedroom, 0, 1);

        var plan = Plan(moveIn, [bathLate, bedroomLate, bedroomEarly]);

        Assert.Equal([bedroomEarly.PhotoId, bedroomLate.PhotoId, bathLate.PhotoId], plan.Select(p => p.MoveOutPhotoId));
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(65, 30)]
    [InlineData(6, -1)]
    public void InvalidSettings_AreRejected(int threshold, int maxCalls)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Plan([], [], threshold, maxCalls));
}
