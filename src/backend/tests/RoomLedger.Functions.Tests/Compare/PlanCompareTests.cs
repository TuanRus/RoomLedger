using Amazon.Lambda.TestUtilities;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Functions.Tests.Photos;
using Plan = RoomLedger.Functions.PlanCompare.Function;

namespace RoomLedger.Functions.Tests.Compare;

/**
 * Lambda plan-compare (MD-06-FE-02, BR-26 RULE 1–2, EXCEPTION 1), bước đầu của Step Functions so ảnh trả phòng.
 * Chạy trong VPC: đọc ảnh READY/SEALED của biên bản nhận phòng và trả phòng, tính pHash trên thumbnail 400 px trong
 * bucket derived, ghép cặp bằng ComparePlanner (ngưỡng và giới hạn gọi đọc từ SSM, Step Functions truyền vào) và trả
 * danh sách cặp cần gọi Bedrock kèm khóa bản xem. Biên bản không còn COMPARING → Proceed = false (chạy lại an toàn).
 *
 * @author Sang
 * @version 1.0
 */
public class PlanCompareTests(PhotoDbFixture db) : IClassFixture<PhotoDbFixture>
{
    private readonly FakeObjectStore _store = new();
    private Plan NewPlan() => new(db.NewDb, _store);

    private sealed record Seeded(CompareScene Scene, Guid BedroomOut, Guid KitchenOut, Guid BathroomOut,
        Guid BedroomIn, Guid KitchenIn, string KitchenInView, string KitchenOutView);

    /**
     * Phòng ngủ: ảnh trả giống hệt ảnh nhận → bỏ qua. Bếp: đồ đạc đổi chỗ → cần so. Phòng tắm: chỉ có ảnh nhận
     * đang xử lý (RECEIVED, không tính) → không có ảnh nhận phòng.
     */
    private async Task<Seeded> SeedAsync(InspectionStatus status = InspectionStatus.Comparing, bool kitchenOutThumbnail = true)
    {
        var s = await new CompareScene(db, _store).BuildAsync(status);
        var bedroomIn = await s.AddPhotoAsync(s.MoveInId, "Phòng ngủ", FileStatus.Sealed, RoomImages.Room());
        var kitchenIn = await s.AddPhotoAsync(s.MoveInId, "Bếp", FileStatus.Sealed, RoomImages.Room());
        await s.AddPhotoAsync(s.MoveInId, "Phòng tắm", FileStatus.Received, RoomImages.Room());
        var bedroomOut = await s.AddPhotoAsync(s.MoveOutId, "Phòng ngủ", FileStatus.Ready, RoomImages.Room());
        var kitchenOut = await s.AddPhotoAsync(s.MoveOutId, "Bếp", FileStatus.Ready, kitchenOutThumbnail ? RoomImages.Room(mirrored: true) : null);
        var bathroomOut = await s.AddPhotoAsync(s.MoveOutId, "Phòng tắm", FileStatus.Ready, RoomImages.Room());
        return new Seeded(s, bedroomOut.Id, kitchenOut.Id, bathroomOut.Id, bedroomIn.Id, kitchenIn.Id, kitchenIn.DerivedKey!, kitchenOut.DerivedKey!);
    }

    private static CompareStart Start(Seeded seed, int threshold = 6, int maxCalls = 30) =>
        new(seed.Scene.MoveOutId, seed.Scene.MoveInId, threshold, maxCalls);

    [Fact]
    public async Task ComparingMoveOut_PairsEveryPhoto_SkipsNearIdentical_AndListsTheRestForBedrock()
    {
        var seed = await SeedAsync();

        var plan = await NewPlan().HandleAsync(Start(seed), new TestLambdaContext());

        Assert.True(plan.Proceed);
        Assert.Equal(seed.Scene.MoveOutId, plan.InspectionId);
        Assert.Equal(seed.Scene.TenancyId, plan.TenancyId);
        Assert.Collection(plan.Pairs,   // theo thứ tự khu vực
            bedroom =>
            {
                Assert.Equal(seed.BedroomOut, bedroom.MoveOutPhotoId);
                Assert.Equal(seed.BedroomIn, bedroom.MoveInPhotoId);
                Assert.Equal(PairDecision.Skipped, bedroom.Decision);
                Assert.Equal(0, bedroom.Distance);
            },
            kitchen =>
            {
                Assert.Equal(seed.KitchenOut, kitchen.MoveOutPhotoId);
                Assert.Equal(PairDecision.Compare, kitchen.Decision);
                Assert.True(kitchen.Distance >= 6);
            },
            bathroom =>
            {
                Assert.Equal(seed.BathroomOut, bathroom.MoveOutPhotoId);
                Assert.Equal(PairDecision.NotAnalyzed, bathroom.Decision);
                Assert.Equal(ComparePlanner.NoMoveInPhoto, bathroom.Reason);
                Assert.Null(bathroom.MoveInPhotoId);
            });
        var task = Assert.Single(plan.ToCompare);
        Assert.Equal(seed.KitchenOut, task.MoveOutPhotoId);
        Assert.Equal(seed.KitchenIn, task.MoveInPhotoId);
        Assert.Equal("Bếp", task.AreaName);
        Assert.Equal(seed.KitchenInView, task.MoveInKey);     // bản xem JPEG, không phải ảnh gốc HEIC
        Assert.Equal(seed.KitchenOutView, task.MoveOutKey);
    }

    [Fact]
    public async Task ThresholdComesFromTheInput_ZeroMeansNothingIsSkipped()
    {
        var seed = await SeedAsync();

        var plan = await NewPlan().HandleAsync(Start(seed, threshold: 0), new TestLambdaContext());

        Assert.Equal(PairDecision.Compare, plan.Pairs.Single(p => p.MoveOutPhotoId == seed.BedroomOut).Decision);
        Assert.Equal(2, plan.ToCompare.Count);
    }

    [Fact]
    public async Task CallLimitComesFromTheInput_OverTheLimitIsNotAnalyzed()
    {
        var seed = await SeedAsync();

        var plan = await NewPlan().HandleAsync(Start(seed, maxCalls: 0), new TestLambdaContext());

        var kitchen = plan.Pairs.Single(p => p.MoveOutPhotoId == seed.KitchenOut);
        Assert.Equal(PairDecision.NotAnalyzed, kitchen.Decision);
        Assert.Equal(ComparePlanner.CallLimit, kitchen.Reason);
        Assert.Empty(plan.ToCompare);
    }

    [Fact]
    public async Task MissingThumbnail_CannotBeFiltered_SoItIsStillCompared()
    {
        var seed = await SeedAsync(kitchenOutThumbnail: false);

        var plan = await NewPlan().HandleAsync(Start(seed), new TestLambdaContext());

        var kitchen = plan.Pairs.Single(p => p.MoveOutPhotoId == seed.KitchenOut);
        Assert.Equal(PairDecision.Compare, kitchen.Decision);
        Assert.Null(kitchen.Distance);
        Assert.Contains(plan.ToCompare, t => t.MoveOutPhotoId == seed.KitchenOut);
    }

    [Theory]
    [InlineData(InspectionStatus.Open)]
    [InlineData(InspectionStatus.Reviewing)]
    public async Task InspectionNotComparing_DoesNothing(InspectionStatus status)
    {
        var seed = await SeedAsync(status);

        var plan = await NewPlan().HandleAsync(Start(seed), new TestLambdaContext());

        Assert.False(plan.Proceed);
        Assert.Empty(plan.Pairs);
        Assert.Empty(plan.ToCompare);
    }

    [Fact]
    public async Task MoveInIdOfAnotherTenancy_IsIgnored_EveryPhotoHasNoMoveInPhoto()
    {
        var seed = await SeedAsync();
        var other = await new CompareScene(db, _store).BuildAsync();
        other.Areas["Bếp"] = seed.Scene.Areas["Bếp"];   // cùng khu vực: nếu không kiểm hợp đồng thì ảnh này sẽ được ghép
        await other.AddPhotoAsync(other.MoveInId, "Bếp", FileStatus.Sealed, RoomImages.Room());

        var plan = await NewPlan().HandleAsync(Start(seed) with { MoveInInspectionId = other.MoveInId }, new TestLambdaContext());

        Assert.All(plan.Pairs, p => Assert.Equal(ComparePlanner.NoMoveInPhoto, p.Reason));
        Assert.Empty(plan.ToCompare);
    }
}
