using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Chủ nhà duyệt gợi ý trả phòng (MD-06-FE-03, BR-27, US-27).
 * Biên bản trả phòng REVIEWING: chủ nhà tính tiền (số nguyên &gt; 0) hoặc bỏ qua từng mục, đổi ý được cho tới khi gửi;
 * thêm mục tay phải gắn một cặp ảnh nhận/trả cùng khu vực; tổng tiền ≤ tiền cọc (400 AMOUNT_EXCEEDS_DEPOSIT);
 * gửi người thuê khi không còn SUGGESTED (409 PENDING_FINDINGS) → AWAITING_CONFIRM, phát FindingsSent.
 * Người thuê chỉ thấy các mục bị tính tiền sau khi đã gửi.
 *
 * @author Sang
 * @version 1.0
 */
public class FindingReviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const long Deposit = 7_000_000;   // Scenario.ValidTerms

    private sealed record Seed(Scenario S, Guid MoveInId, Guid MoveOutId, Guid AreaId, Guid MoveInPhotoId, Guid MoveOutPhotoId);

    /** Hợp đồng ACTIVE, nhận phòng LOCKED, trả phòng REVIEWING; mỗi biên bản một ảnh READY ở khu vực đầu tiên. */
    private async Task<Seed> SeedAsync(InspectionStatus moveOutStatus = InspectionStatus.Reviewing)
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var moveIn = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        var moveInId = moveIn.GetProperty("inspectionId").GetGuid();
        var areaId = moveIn.GetProperty("areas")[0].GetProperty("areaId").GetGuid();
        await SetStatusAsync(moveInId, InspectionStatus.Locked);
        var moveOut = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-out", null)).DataAsync();
        var moveOutId = moveOut.GetProperty("inspectionId").GetGuid();
        await SetStatusAsync(moveOutId, moveOutStatus);
        var tenantId = await factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == s.TenancyId).Select(t => t.TenantId!.Value).SingleAsync());
        var inPhoto = await AddPhotoAsync(moveInId, areaId, tenantId);
        var outPhoto = await AddPhotoAsync(moveOutId, areaId, tenantId);
        return new Seed(s, moveInId, moveOutId, areaId, inPhoto, outPhoto);
    }

    private Task SetStatusAsync(Guid id, InspectionStatus status) =>
        factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == id).ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, status)));

    private async Task<Guid> AddPhotoAsync(Guid inspectionId, Guid areaId, Guid uploadedBy)
    {
        var photo = new Photo
        {
            InspectionId = inspectionId, RoomAreaId = areaId, UploadedBy = uploadedBy, UploaderRole = UserRole.Tenant,
            FileName = "a.jpg", ContentType = "image/jpeg", Size = 1000, Sha256 = Scenario.Sha,
            IncomingKey = $"k/{Guid.NewGuid()}.jpg", Status = FileStatus.Ready,
        };
        await factory.WithDbAsync(async db => { db.Photos.Add(photo); await db.SaveChangesAsync(); });
        return photo.Id;
    }

    /** Một kết quả AI như persist-results sẽ ghi. */
    private async Task<Guid> AddFindingAsync(Seed seed, FindingStatus status = FindingStatus.Suggested, string title = "Vết bẩn trên tường")
    {
        var finding = new Finding
        {
            InspectionId = seed.MoveOutId, RoomAreaId = seed.AreaId, MoveInPhotoId = seed.MoveInPhotoId,
            MoveOutPhotoId = seed.MoveOutPhotoId, PhashDistance = 14, Status = status, Title = title,
            Description = "Mảng ố vàng cạnh cửa sổ.", BoundingBoxJson = """{"x":0.1,"y":0.2,"width":0.3,"height":0.25}""",
            Confidence = 0.82m, ModelId = "au.anthropic.claude-sonnet-4-5-20250929-v1:0",
        };
        await factory.WithDbAsync(async db => { db.Findings.Add(finding); await db.SaveChangesAsync(); });
        return finding.Id;
    }

    private Task<Finding> FindingAsync(Guid id) => factory.WithDbAsync(db => db.Findings.AsNoTracking().SingleAsync(f => f.Id == id));

    private static async Task<JsonElement[]> ListAsync(HttpClient client, Guid inspectionId)
    {
        var res = await client.GetAsync($"/api/v1/inspections/{inspectionId}/findings");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.DataAsync()).EnumerateArray().ToArray();
    }

    private static Task<HttpResponseMessage> Charge(HttpClient c, Guid id, object amount) =>
        c.PostJsonAsync($"/api/v1/findings/{id}/charge", new { amount });

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    private Task<int> AuditCount(Guid targetId, string type) =>
        factory.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.TargetId == targetId && a.EventType == type));

    // ---------- Xem ----------

    [Fact]
    public async Task Landlord_SeesEveryResult_WithItsDetails()
    {
        var seed = await SeedAsync();
        var id = await AddFindingAsync(seed);
        await AddFindingAsync(seed, FindingStatus.Skipped, "Cặp gần giống");
        await AddFindingAsync(seed, FindingStatus.NotAnalyzed, "Chưa phân tích");

        var list = await ListAsync(seed.S.LandlordClient, seed.MoveOutId);

        Assert.Equal(3, list.Length);
        var f = list.Single(x => x.GetProperty("findingId").GetGuid() == id);
        Assert.Equal("SUGGESTED", f.GetProperty("status").GetString());
        Assert.Equal("Vết bẩn trên tường", f.GetProperty("title").GetString());
        Assert.Equal(seed.AreaId, f.GetProperty("areaId").GetGuid());
        Assert.Equal(seed.MoveInPhotoId, f.GetProperty("moveInPhotoId").GetGuid());
        Assert.Equal(seed.MoveOutPhotoId, f.GetProperty("moveOutPhotoId").GetGuid());
        Assert.Equal(0.3, f.GetProperty("boundingBox").GetProperty("width").GetDouble(), 3);
        Assert.Equal(0.82, f.GetProperty("confidence").GetDouble(), 3);
        Assert.False(f.GetProperty("isManual").GetBoolean());
        Assert.Equal(JsonValueKind.Null, f.GetProperty("amount").ValueKind);
    }

    [Fact]
    public async Task Tenant_SeesNothing_BeforeTheListIsSent()
    {
        var seed = await SeedAsync();
        await AddFindingAsync(seed);
        Assert.Empty(await ListAsync(seed.S.TenantClient, seed.MoveOutId));
    }

    [Fact]
    public async Task Stranger_Gets404_OnTheList()
    {
        var seed = await SeedAsync();
        var stranger = factory.ClientFor(TestUser.Landlord($"x-{Guid.NewGuid():N}@example.com"));
        await AssertError(await stranger.GetAsync($"/api/v1/inspections/{seed.MoveOutId}/findings"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    // ---------- Tính tiền / bỏ qua ----------

    [Fact]
    public async Task Charge_SetsCharged_AndTheAmount_AndIsAudited()
    {
        var seed = await SeedAsync();
        var id = await AddFindingAsync(seed);

        var res = await Charge(seed.S.LandlordClient, id, 500_000);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("CHARGED", (await res.DataAsync()).GetProperty("status").GetString());
        var f = await FindingAsync(id);
        Assert.Equal(FindingStatus.Charged, f.Status);
        Assert.Equal(500_000, f.Amount);
        Assert.Equal(1, await AuditCount(id, "FINDING_CHARGED"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public async Task Charge_NonPositiveAmount_Gets400(long amount)
    {
        var seed = await SeedAsync();
        var id = await AddFindingAsync(seed);
        await AssertError(await Charge(seed.S.LandlordClient, id, amount), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(FindingStatus.Suggested, (await FindingAsync(id)).Status);
    }

    [Fact]
    public async Task Charge_TotalAboveTheDeposit_Gets400_AmountExceedsDeposit()
    {
        var seed = await SeedAsync();
        var first = await AddFindingAsync(seed);
        var second = await AddFindingAsync(seed);
        await Charge(seed.S.LandlordClient, first, 6_000_000);

        await AssertError(await Charge(seed.S.LandlordClient, second, 1_000_001), HttpStatusCode.BadRequest, "AMOUNT_EXCEEDS_DEPOSIT");
        Assert.Equal(HttpStatusCode.OK, (await Charge(seed.S.LandlordClient, second, 1_000_000)).StatusCode);   // đúng bằng tiền cọc
    }

    [Fact]
    public async Task Recharging_TheSameFinding_ReplacesItsAmount_InTheDepositCheck()
    {
        var seed = await SeedAsync();
        var id = await AddFindingAsync(seed);
        await Charge(seed.S.LandlordClient, id, 6_000_000);

        Assert.Equal(HttpStatusCode.OK, (await Charge(seed.S.LandlordClient, id, Deposit)).StatusCode);
        Assert.Equal(Deposit, (await FindingAsync(id)).Amount);
    }

    [Fact]
    public async Task Dismiss_SetsDismissed_ClearsTheAmount_AndIsAudited()
    {
        var seed = await SeedAsync();
        var id = await AddFindingAsync(seed);
        await Charge(seed.S.LandlordClient, id, 200_000);   // đổi ý

        var res = await seed.S.LandlordClient.PostAsync($"/api/v1/findings/{id}/dismiss", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var f = await FindingAsync(id);
        Assert.Equal(FindingStatus.Dismissed, f.Status);
        Assert.Null(f.Amount);
        Assert.Equal(1, await AuditCount(id, "FINDING_DISMISSED"));
    }

    [Theory]
    [InlineData(FindingStatus.Skipped)]
    [InlineData(FindingStatus.NotAnalyzed)]
    public async Task Landlord_CanStillCharge_SkippedOrNotAnalyzedPairs(FindingStatus status)
    {
        var seed = await SeedAsync();
        var id = await AddFindingAsync(seed, status);
        Assert.Equal(HttpStatusCode.OK, (await Charge(seed.S.LandlordClient, id, 100_000)).StatusCode);
    }

    [Fact]
    public async Task Tenant_Gets403_Stranger_Gets404()
    {
        var seed = await SeedAsync();
        var id = await AddFindingAsync(seed);
        var stranger = factory.ClientFor(TestUser.Landlord($"x-{Guid.NewGuid():N}@example.com"));

        await AssertError(await Charge(seed.S.TenantClient, id, 1000), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertError(await seed.S.TenantClient.PostAsync($"/api/v1/findings/{id}/dismiss", null), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertError(await Charge(stranger, id, 1000), HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Equal(FindingStatus.Suggested, (await FindingAsync(id)).Status);
    }

    [Fact]
    public async Task NotReviewingYet_Gets409_InvalidState()
    {
        var seed = await SeedAsync(InspectionStatus.Comparing);
        var id = await AddFindingAsync(seed);
        await AssertError(await Charge(seed.S.LandlordClient, id, 1000), HttpStatusCode.Conflict, "INVALID_STATE");
    }

    // ---------- Mục tay ----------

    [Fact]
    public async Task ManualItem_WithAPhotoPair_IsCreatedCharged()
    {
        var seed = await SeedAsync();

        var res = await seed.S.LandlordClient.PostJsonAsync($"/api/v1/inspections/{seed.MoveOutId}/findings", new
        {
            areaId = seed.AreaId, title = "Mất rèm cửa", description = "Rèm cửa sổ không còn.", amount = 300_000,
            moveInPhotoId = seed.MoveInPhotoId, moveOutPhotoId = seed.MoveOutPhotoId,
        });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.DataAsync()).GetProperty("findingId").GetGuid();
        var f = await FindingAsync(id);
        Assert.True(f.IsManual);
        Assert.Equal(FindingStatus.Charged, f.Status);
        Assert.Equal(300_000, f.Amount);
        Assert.Equal(seed.MoveOutId, f.InspectionId);
        Assert.Equal(1, await AuditCount(id, "FINDING_ADDED"));
    }

    [Fact]
    public async Task ManualItem_WithoutAPhotoPair_Gets400()
    {
        var seed = await SeedAsync();
        await AssertError(await seed.S.LandlordClient.PostJsonAsync($"/api/v1/inspections/{seed.MoveOutId}/findings", new
        {
            areaId = seed.AreaId, title = "Mất rèm", amount = 300_000, moveOutPhotoId = seed.MoveOutPhotoId,
        }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task ManualItem_PhotosMustBeFromTheMoveInAndMoveOutOfThisTenancy()
    {
        var seed = await SeedAsync();
        await AssertError(await seed.S.LandlordClient.PostJsonAsync($"/api/v1/inspections/{seed.MoveOutId}/findings", new
        {
            areaId = seed.AreaId, title = "Đảo ảnh", amount = 1000,
            moveInPhotoId = seed.MoveOutPhotoId, moveOutPhotoId = seed.MoveInPhotoId,   // đảo chỗ
        }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task ManualItem_AboveTheDeposit_Gets400_AmountExceedsDeposit()
    {
        var seed = await SeedAsync();
        await AssertError(await seed.S.LandlordClient.PostJsonAsync($"/api/v1/inspections/{seed.MoveOutId}/findings", new
        {
            areaId = seed.AreaId, title = "Quá cọc", amount = Deposit + 1,
            moveInPhotoId = seed.MoveInPhotoId, moveOutPhotoId = seed.MoveOutPhotoId,
        }), HttpStatusCode.BadRequest, "AMOUNT_EXCEEDS_DEPOSIT");
    }

    // ---------- Gửi người thuê ----------

    [Fact]
    public async Task Send_WithAnUndecidedSuggestion_Gets409_PendingFindings()
    {
        var seed = await SeedAsync();
        var decided = await AddFindingAsync(seed);
        await AddFindingAsync(seed);
        await Charge(seed.S.LandlordClient, decided, 100_000);

        var res = await seed.S.LandlordClient.PostAsync($"/api/v1/inspections/{seed.MoveOutId}/send-to-tenant", null);

        await AssertError(res, HttpStatusCode.Conflict, "PENDING_FINDINGS");
        Assert.Equal(InspectionStatus.Reviewing, await factory.WithDbAsync(db =>
            db.Inspections.Where(i => i.Id == seed.MoveOutId).Select(i => i.Status).SingleAsync()));
    }

    [Fact]
    public async Task Send_WhenAllDecided_MovesToAwaitingConfirm_NotifiesTheTenant_AndTheTenantSeesTheChargedItems()
    {
        var seed = await SeedAsync();
        var charged = await AddFindingAsync(seed, title: "Vết nứt gạch");
        var dismissed = await AddFindingAsync(seed, title: "Lệch góc");
        await AddFindingAsync(seed, FindingStatus.Skipped);       // không chặn việc gửi
        await AddFindingAsync(seed, FindingStatus.NotAnalyzed);   // không chặn việc gửi
        await Charge(seed.S.LandlordClient, charged, 400_000);
        await seed.S.LandlordClient.PostAsync($"/api/v1/findings/{dismissed}/dismiss", null);

        var res = await seed.S.LandlordClient.PostAsync($"/api/v1/inspections/{seed.MoveOutId}/send-to-tenant", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("AWAITING_CONFIRM", (await res.DataAsync()).GetProperty("status").GetString());
        var sent = factory.Events.Last("FindingsSent");
        Assert.Equal(seed.MoveOutId, sent.GetProperty("inspectionId").GetGuid());
        Assert.Equal(400_000, sent.GetProperty("total").GetInt64());
        Assert.Equal(1, await AuditCount(seed.MoveOutId, "FINDINGS_SENT"));
        var tenantView = Assert.Single(await ListAsync(seed.S.TenantClient, seed.MoveOutId));
        Assert.Equal(charged, tenantView.GetProperty("findingId").GetGuid());
    }

    [Fact]
    public async Task AfterSending_TheLandlordCanNoLongerChangeItems()
    {
        var seed = await SeedAsync();
        var id = await AddFindingAsync(seed);
        await Charge(seed.S.LandlordClient, id, 100_000);
        await seed.S.LandlordClient.PostAsync($"/api/v1/inspections/{seed.MoveOutId}/send-to-tenant", null);

        await AssertError(await Charge(seed.S.LandlordClient, id, 200_000), HttpStatusCode.Conflict, "RECORD_LOCKED");
        await AssertError(await seed.S.LandlordClient.PostAsync($"/api/v1/findings/{id}/dismiss", null), HttpStatusCode.Conflict, "RECORD_LOCKED");
    }

    [Fact]
    public async Task SendingTwice_Returns200_ButNotifiesOnce()
    {
        var seed = await SeedAsync();
        await seed.S.LandlordClient.PostAsync($"/api/v1/inspections/{seed.MoveOutId}/send-to-tenant", null);

        var again = await seed.S.LandlordClient.PostAsync($"/api/v1/inspections/{seed.MoveOutId}/send-to-tenant", null);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, factory.Events.Published.Count(e => e.DetailType == "FindingsSent"
            && e.Detail.GetProperty("inspectionId").GetGuid() == seed.MoveOutId));
    }

    [Fact]
    public async Task Tenant_CannotSend_Gets403()
    {
        var seed = await SeedAsync();
        await AssertError(await seed.S.TenantClient.PostAsync($"/api/v1/inspections/{seed.MoveOutId}/send-to-tenant", null),
            HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
