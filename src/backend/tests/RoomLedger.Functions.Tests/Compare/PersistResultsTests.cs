using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.TestUtilities;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Ai.Compare;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Functions.Tests.Photos;
using Persist = RoomLedger.Functions.PersistResults.Function;

namespace RoomLedger.Functions.Tests.Compare;

/**
 * Lambda persist-results (MD-06-FE-02, BR-26 RULE 1–3, EXCEPTION 1–2), bước cuối của Step Functions so ảnh.
 * Trong một transaction: UPDATE có điều kiện COMPARING → REVIEWING, ghi bảng finding (mỗi phát hiện AI một dòng
 * SUGGESTED; cặp bỏ qua một dòng SKIPPED; cặp không phân tích được một dòng NOT_ANALYZED kèm lý do), audit
 * MOVE_OUT_COMPARED. Biên bản không còn COMPARING → không ghi gì (Step Functions chạy lại an toàn).
 * Trả số đếm cho bước PutMetricData và sự kiện MoveOutCompared.
 *
 * @author Sang
 * @version 1.0
 */
public class PersistResultsTests(PhotoDbFixture db) : IClassFixture<PhotoDbFixture>
{
    private const string Sonnet = "au.anthropic.claude-sonnet-4-5-20250929-v1:0";
    private const string Haiku = "au.anthropic.claude-haiku-4-5-20251001-v1:0";
    private readonly FakeObjectStore _store = new();

    private static readonly AiFinding Stain = new("Vết bẩn trên tường", "Mảng ố vàng lớn cạnh cửa sổ.", "Phòng ngủ",
        new BoundingBox(0.1, 0.2, 0.3, 0.25), 0.82);
    private static readonly AiFinding Crack = new("Nứt gạch", "Viên gạch cạnh giường bị nứt.", "Phòng ngủ",
        new BoundingBox(0.5, 0.6, 0.2, 0.1), 0.6);

    private sealed record Seeded(CompareScene Scene, PersistInput Input, Guid AnalyzedOut, Guid CleanOut, Guid SkippedOut,
        Guid NoMoveInOut, Guid LimitOut, Guid AiFailedOut, Guid NoResultOut, Guid BedroomIn, Guid KitchenIn);

    /**
     * Bảy ảnh trả phòng: có 2 phát hiện (mô hình dự phòng), phân tích không thấy gì, bỏ qua, không có ảnh nhận phòng,
     * vượt giới hạn gọi, AI lỗi, và cặp cần so nhưng Map không trả kết quả.
     */
    private async Task<Seeded> SeedAsync(InspectionStatus status = InspectionStatus.Comparing)
    {
        var s = await new CompareScene(db, _store).BuildAsync(status);
        var bedroomIn = (await s.AddPhotoAsync(s.MoveInId, "Phòng ngủ", FileStatus.Sealed, null)).Id;
        var kitchenIn = (await s.AddPhotoAsync(s.MoveInId, "Bếp", FileStatus.Sealed, null)).Id;
        async Task<Guid> Out(string area) => (await s.AddPhotoAsync(s.MoveOutId, area, FileStatus.Ready, null)).Id;
        var (analyzed, clean, skipped, noMoveIn, limit, aiFailed, noResult) =
            (await Out("Phòng ngủ"), await Out("Bếp"), await Out("Bếp"), await Out("Phòng tắm"), await Out("Phòng ngủ"),
             await Out("Phòng ngủ"), await Out("Bếp"));
        var bed = s.Areas["Phòng ngủ"];
        var kit = s.Areas["Bếp"];
        PlannedPair[] pairs =
        [
            new(analyzed, bed, bedroomIn, 20, PairDecision.Compare, null),
            new(clean, kit, kitchenIn, 15, PairDecision.Compare, null),
            new(skipped, kit, kitchenIn, 2, PairDecision.Skipped, null),
            new(noMoveIn, s.Areas["Phòng tắm"], null, null, PairDecision.NotAnalyzed, ComparePlanner.NoMoveInPhoto),
            new(limit, bed, bedroomIn, 30, PairDecision.NotAnalyzed, ComparePlanner.CallLimit),
            new(aiFailed, bed, bedroomIn, 25, PairDecision.Compare, null),
            new(noResult, kit, kitchenIn, 12, PairDecision.Compare, null),
        ];
        PairResult[] results =
        [
            new(analyzed, PairResult.Analyzed, [Stain, Crack], Haiku, true),
            new(clean, PairResult.Analyzed, [], Sonnet, false),
            new(aiFailed, PairResult.NotAnalyzed, [], null, false),
        ];
        return new Seeded(s, new PersistInput(s.MoveOutId, pairs, results), analyzed, clean, skipped, noMoveIn, limit, aiFailed,
            noResult, bedroomIn, kitchenIn);
    }

    private async Task<List<Finding>> FindingsAsync(Guid inspectionId)
    {
        await using var ctx = db.NewDb();
        return await ctx.Findings.AsNoTracking().Where(f => f.InspectionId == inspectionId).ToListAsync();
    }

    private async Task<Inspection> InspectionAsync(Guid id)
    {
        await using var ctx = db.NewDb();
        return await ctx.Inspections.AsNoTracking().SingleAsync(i => i.Id == id);
    }

    private async Task<int> AuditCount(Guid id)
    {
        await using var ctx = db.NewDb();
        return await ctx.AuditEvents.CountAsync(a => a.TargetId == id && a.EventType == "MOVE_OUT_COMPARED");
    }

    [Fact]
    public async Task WritesOneSuggestedRowPerAiFinding_WithEverythingTheLandlordNeeds()
    {
        var seed = await SeedAsync();

        await new Persist(db.NewDb).HandleAsync(seed.Input, new TestLambdaContext());

        var rows = (await FindingsAsync(seed.Scene.MoveOutId)).Where(f => f.Status == FindingStatus.Suggested).OrderBy(f => f.Title).ToList();
        Assert.Equal(["Nứt gạch", "Vết bẩn trên tường"], rows.Select(r => r.Title));
        var stain = rows[1];
        Assert.Equal("Mảng ố vàng lớn cạnh cửa sổ.", stain.Description);
        Assert.Equal(seed.Scene.Areas["Phòng ngủ"], stain.RoomAreaId);
        Assert.Equal(seed.AnalyzedOut, stain.MoveOutPhotoId);
        Assert.Equal(seed.BedroomIn, stain.MoveInPhotoId);
        Assert.Equal(20, stain.PhashDistance);
        Assert.Equal(0.82m, stain.Confidence);
        Assert.Equal(Haiku, stain.ModelId);
        Assert.True(stain.UsedFallback);
        Assert.False(stain.IsManual);
        Assert.Null(stain.Amount);
        using var box = JsonDocument.Parse(stain.BoundingBoxJson!);
        Assert.Equal(0.1, box.RootElement.GetProperty("x").GetDouble(), 3);
        Assert.Equal(0.25, box.RootElement.GetProperty("height").GetDouble(), 3);
    }

    [Fact]
    public async Task SkippedAndNotAnalyzedPairs_GetOneRowEach_AnalyzedWithoutFindings_GetsNone()
    {
        var seed = await SeedAsync();

        await new Persist(db.NewDb).HandleAsync(seed.Input, new TestLambdaContext());

        var rows = await FindingsAsync(seed.Scene.MoveOutId);
        Assert.DoesNotContain(rows, r => r.MoveOutPhotoId == seed.CleanOut);
        var skipped = Assert.Single(rows, r => r.MoveOutPhotoId == seed.SkippedOut);
        Assert.Equal(FindingStatus.Skipped, skipped.Status);
        Assert.Equal(2, skipped.PhashDistance);
        Assert.Equal(seed.KitchenIn, skipped.MoveInPhotoId);

        Finding NotAnalyzed(Guid photo) => Assert.Single(rows, r => r.MoveOutPhotoId == photo && r.Status == FindingStatus.NotAnalyzed);
        Assert.Contains("ảnh nhận phòng", NotAnalyzed(seed.NoMoveInOut).Description);
        Assert.Null(NotAnalyzed(seed.NoMoveInOut).MoveInPhotoId);
        Assert.Contains("giới hạn", NotAnalyzed(seed.LimitOut).Description);
        Assert.Contains("AI", NotAnalyzed(seed.AiFailedOut).Description);
        Assert.Contains("AI", NotAnalyzed(seed.NoResultOut).Description);   // Map không trả kết quả = AI lỗi
        Assert.Equal(7, rows.Count);   // 2 gợi ý + 1 bỏ qua + 4 không phân tích
    }

    [Fact]
    public async Task MovesTheInspectionToReviewing_Audits_AndReturnsTheCounts()
    {
        var seed = await SeedAsync();

        var output = await new Persist(db.NewDb).HandleAsync(seed.Input, new TestLambdaContext());

        Assert.Equal(InspectionStatus.Reviewing, (await InspectionAsync(seed.Scene.MoveOutId)).Status);
        Assert.Equal(1, await AuditCount(seed.Scene.MoveOutId));
        Assert.True(output.Persisted);
        Assert.Equal(seed.Scene.MoveOutId, output.InspectionId);
        Assert.Equal(seed.Scene.TenancyId, output.TenancyId);
        Assert.Equal(2, output.Analyzed);      // cặp Bedrock trả lời hợp lệ
        Assert.Equal(2, output.Suggested);     // số dòng gợi ý
        Assert.Equal(1, output.Skipped);
        Assert.Equal(4, output.NotAnalyzed);
        Assert.Equal(1, output.UsedFallback);
    }

    [Fact]
    public async Task RunningTwice_WritesOnce()
    {
        var seed = await SeedAsync();
        await new Persist(db.NewDb).HandleAsync(seed.Input, new TestLambdaContext());

        var again = await new Persist(db.NewDb).HandleAsync(seed.Input, new TestLambdaContext());

        Assert.False(again.Persisted);
        Assert.Equal(7, (await FindingsAsync(seed.Scene.MoveOutId)).Count);
        Assert.Equal(1, await AuditCount(seed.Scene.MoveOutId));
    }

    [Fact]
    public async Task InspectionNotComparing_WritesNothing()
    {
        var seed = await SeedAsync(InspectionStatus.Open);

        var output = await new Persist(db.NewDb).HandleAsync(seed.Input, new TestLambdaContext());

        Assert.False(output.Persisted);
        Assert.Empty(await FindingsAsync(seed.Scene.MoveOutId));
        Assert.Equal(InspectionStatus.Open, (await InspectionAsync(seed.Scene.MoveOutId)).Status);
        Assert.Equal(0, await AuditCount(seed.Scene.MoveOutId));
    }

    /**
     * Hình dạng JSON Step Functions ghép: Pairs lấy nguyên từ output plan-compare, Results gồm output compare-pairs và
     * phần tử do bước Catch tạo (chỉ MoveOutPhotoId, Status = NOT_ANALYZED). Đi qua serializer thật của Lambda.
     */
    [Fact]
    public async Task StateMachineShapedJson_RoundTripsThroughTheLambdaSerializer()
    {
        var seed = await SeedAsync();
        var serializer = new DefaultLambdaJsonSerializer();
        JsonNode Serialize<T>(T value)
        {
            using var ms = new MemoryStream();
            serializer.Serialize(value, ms);
            return JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray()))!;
        }
        var plan = Serialize(new ComparePlan(true, seed.Scene.MoveOutId, seed.Scene.TenancyId, seed.Input.Pairs, []));
        var analyzed = Serialize(seed.Input.Results[0]);
        var caught = new JsonObject { ["MoveOutPhotoId"] = seed.AiFailedOut.ToString(), ["Status"] = "NOT_ANALYZED" };
        var state = new JsonObject
        {
            ["InspectionId"] = plan["InspectionId"]!.DeepClone(),
            ["Pairs"] = plan["Pairs"]!.DeepClone(),
            ["Results"] = new JsonArray(analyzed, caught),
        };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(state.ToJsonString()));

        var output = await new Persist(db.NewDb).HandleAsync(serializer.Deserialize<PersistInput>(input), new TestLambdaContext());

        Assert.True(output.Persisted);
        Assert.Equal(2, output.Suggested);
        Assert.Equal(1, output.Skipped);
        Assert.Equal(5, output.NotAnalyzed);   // ví dụ này không đưa kết quả của "clean" → coi như AI lỗi
        Assert.Contains("\"Persisted\":true", Serialize(output).ToJsonString());   // bước Choice đọc $.Persisted
    }
}
