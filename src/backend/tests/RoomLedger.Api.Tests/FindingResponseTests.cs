using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Người thuê đồng ý hoặc khiếu nại từng mục bị trừ cọc (MD-06-FE-04, BR-28, US-28).
 * Sau khi chủ nhà gửi (AWAITING_CONFIRM): người thuê đồng ý (CHARGED → ACCEPTED) hoặc khiếu nại với lý do ≥ 10 ký tự
 * (→ DISPUTED, tạo dispute FINDING, báo chủ nhà). Chủ nhà trả lời: sửa số tiền (CORRECTED), giữ nguyên kèm phản hồi
 * (KEPT) hoặc bỏ mục (REMOVED → DISMISSED, khiếu nại đóng). Sau CORRECTED/KEPT người thuê đồng ý lại → RESOLVED.
 *
 * @author Sang
 * @version 1.0
 */
public class FindingResponseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const long Deposit = 7_000_000;   // Scenario.ValidTerms

    private sealed record Seed(Scenario S, Guid MoveOutId, Guid FindingId, Guid LandlordId, Guid TenantId);

    /** Trả phòng REVIEWING, một mục được tính 500.000 đ, rồi (mặc định) gửi người thuê. */
    private async Task<Seed> SeedAsync(bool send = true, long amount = 500_000)
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var moveIn = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        var areaId = moveIn.GetProperty("areas")[0].GetProperty("areaId").GetGuid();
        await SetStatusAsync(moveIn.GetProperty("inspectionId").GetGuid(), InspectionStatus.Locked);
        var moveOut = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-out", null)).DataAsync();
        var moveOutId = moveOut.GetProperty("inspectionId").GetGuid();
        await SetStatusAsync(moveOutId, InspectionStatus.Reviewing);
        var finding = new Finding { InspectionId = moveOutId, RoomAreaId = areaId, Title = "Vết bẩn trên tường", Status = FindingStatus.Suggested };
        await factory.WithDbAsync(async db => { db.Findings.Add(finding); return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PostJsonAsync($"/api/v1/findings/{finding.Id}/charge", new { amount })).StatusCode);
        if (send)
        {
            Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PostAsync($"/api/v1/inspections/{moveOutId}/send-to-tenant", null)).StatusCode);
        }
        var ids = await factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == s.TenancyId)
            .Select(t => new { t.LandlordId, TenantId = t.TenantId!.Value }).SingleAsync());
        return new Seed(s, moveOutId, finding.Id, ids.LandlordId, ids.TenantId);
    }

    private Task SetStatusAsync(Guid id, InspectionStatus status) =>
        factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == id).ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, status)));

    private Task<Finding> FindingAsync(Guid id) => factory.WithDbAsync(db => db.Findings.AsNoTracking().SingleAsync(f => f.Id == id));

    private Task<Dispute?> DisputeAsync(Guid findingId) => factory.WithDbAsync(db => db.Disputes.AsNoTracking()
        .SingleOrDefaultAsync(d => d.TargetType == DisputeTargetType.Finding && d.TargetId == findingId));

    private static Task<HttpResponseMessage> Accept(HttpClient c, Guid id) => c.PostAsync($"/api/v1/findings/{id}/accept", null);

    private static Task<HttpResponseMessage> Dispute(HttpClient c, Guid id, string reason) =>
        c.PostJsonAsync($"/api/v1/findings/{id}/dispute", new { reason });

    private static Task<HttpResponseMessage> Respond(HttpClient c, Guid id, object body) =>
        c.PostJsonAsync($"/api/v1/findings/{id}/respond", body);

    private const string GoodReason = "Vết này có từ lúc nhận phòng";

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    private Task<int> AuditCount(Guid targetId, string type) =>
        factory.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.TargetId == targetId && a.EventType == type));

    // ---------- Người thuê ----------

    [Fact]
    public async Task Tenant_Accepts_ChargedItem_BecomesAccepted()
    {
        var seed = await SeedAsync();

        var res = await Accept(seed.S.TenantClient, seed.FindingId);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("ACCEPTED", (await res.DataAsync()).GetProperty("status").GetString());
        Assert.Equal(FindingStatus.Accepted, (await FindingAsync(seed.FindingId)).Status);
        Assert.Equal(1, await AuditCount(seed.FindingId, "FINDING_ACCEPTED"));
    }

    [Fact]
    public async Task Tenant_Disputes_WithAReason_CreatesADispute_AndNotifiesTheLandlord()
    {
        var seed = await SeedAsync();

        var res = await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(FindingStatus.Disputed, (await FindingAsync(seed.FindingId)).Status);
        var d = await DisputeAsync(seed.FindingId);
        Assert.NotNull(d);
        Assert.Equal(DisputeStatus.Open, d!.Status);
        Assert.Equal(GoodReason, d.Reason);
        Assert.Equal(seed.TenantId, d.RaisedBy);
        Assert.Equal(seed.S.TenancyId, d.TenancyId);
        var evt = factory.Events.Last("FindingDisputed");
        Assert.Equal(seed.FindingId, evt.GetProperty("findingId").GetGuid());
        Assert.Equal(seed.LandlordId, evt.GetProperty("notifyUserId").GetGuid());
        Assert.Equal(1, await AuditCount(seed.FindingId, "FINDING_DISPUTED"));
    }

    [Fact]
    public async Task Dispute_ReasonShorterThanTenCharacters_Gets400()
    {
        var seed = await SeedAsync();
        await AssertError(await Dispute(seed.S.TenantClient, seed.FindingId, "Có từ lâu"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(FindingStatus.Charged, (await FindingAsync(seed.FindingId)).Status);
        Assert.Null(await DisputeAsync(seed.FindingId));
    }

    [Fact]
    public async Task Landlord_CannotAcceptOrDispute_Gets403()
    {
        var seed = await SeedAsync();
        await AssertError(await Accept(seed.S.LandlordClient, seed.FindingId), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertError(await Dispute(seed.S.LandlordClient, seed.FindingId, GoodReason), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task ItemOfAnotherTenancy_Gets404()   // BR-28 EXCEPTION 1
    {
        var seed = await SeedAsync();
        var otherTenant = factory.ClientFor(TestUser.Tenant($"other-{Guid.NewGuid():N}@example.com"));
        await AssertError(await Accept(otherTenant, seed.FindingId), HttpStatusCode.NotFound, "NOT_FOUND");
        await AssertError(await Dispute(otherTenant, seed.FindingId, GoodReason), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task BeforeTheListIsSent_TenantCannotRespond_Gets409()
    {
        var seed = await SeedAsync(send: false);
        await AssertError(await Accept(seed.S.TenantClient, seed.FindingId), HttpStatusCode.Conflict, "INVALID_STATE");
    }

    [Fact]
    public async Task AcceptingTwice_Gets409_InvalidState()
    {
        var seed = await SeedAsync();
        await Accept(seed.S.TenantClient, seed.FindingId);
        await AssertError(await Accept(seed.S.TenantClient, seed.FindingId), HttpStatusCode.Conflict, "INVALID_STATE");
        await AssertError(await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason), HttpStatusCode.Conflict, "INVALID_STATE");
    }

    [Fact]
    public async Task DisputedItem_CannotBeAccepted_BeforeTheLandlordAnswers()
    {
        var seed = await SeedAsync();
        await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);
        await AssertError(await Accept(seed.S.TenantClient, seed.FindingId), HttpStatusCode.Conflict, "INVALID_STATE");
    }

    // ---------- Chủ nhà trả lời ----------

    [Fact]
    public async Task Landlord_CorrectsTheAmount_ThenTenantAccepts_Resolved()
    {
        var seed = await SeedAsync();
        await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);

        var res = await Respond(seed.S.LandlordClient, seed.FindingId, new { action = "CORRECTED", amount = 200_000, response = "Giảm vì vết nhỏ" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var f = await FindingAsync(seed.FindingId);
        Assert.Equal(FindingStatus.Disputed, f.Status);   // chờ người thuê đồng ý lại
        Assert.Equal(200_000, f.Amount);
        var d = await DisputeAsync(seed.FindingId);
        Assert.Equal(DisputeResolution.Corrected, d!.Resolution);
        Assert.Equal(DisputeStatus.Open, d.Status);
        Assert.Equal(seed.TenantId, factory.Events.Last("DisputeAnswered").GetProperty("notifyUserId").GetGuid());

        Assert.Equal(HttpStatusCode.OK, (await Accept(seed.S.TenantClient, seed.FindingId)).StatusCode);

        Assert.Equal(FindingStatus.Resolved, (await FindingAsync(seed.FindingId)).Status);
        d = await DisputeAsync(seed.FindingId);
        Assert.Equal(DisputeStatus.Resolved, d!.Status);
        Assert.NotNull(d.ResolvedAt);
    }

    [Fact]
    public async Task Landlord_KeepsTheAmount_WithAReply_ThenTenantAccepts_Resolved()
    {
        var seed = await SeedAsync();
        await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);

        Assert.Equal(HttpStatusCode.OK, (await Respond(seed.S.LandlordClient, seed.FindingId,
            new { action = "KEPT", response = "Ảnh nhận phòng không có vết này" })).StatusCode);
        var d = await DisputeAsync(seed.FindingId);
        Assert.Equal(DisputeResolution.Kept, d!.Resolution);
        Assert.Equal("Ảnh nhận phòng không có vết này", d.Response);

        await Accept(seed.S.TenantClient, seed.FindingId);
        var f = await FindingAsync(seed.FindingId);
        Assert.Equal(FindingStatus.Resolved, f.Status);
        Assert.Equal(500_000, f.Amount);
    }

    [Fact]
    public async Task Landlord_RemovesTheItem_DisputeClosed_NothingDeducted()
    {
        var seed = await SeedAsync();
        await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);

        Assert.Equal(HttpStatusCode.OK, (await Respond(seed.S.LandlordClient, seed.FindingId, new { action = "REMOVED" })).StatusCode);

        var f = await FindingAsync(seed.FindingId);
        Assert.Equal(FindingStatus.Dismissed, f.Status);
        Assert.Null(f.Amount);
        Assert.Equal(DisputeStatus.Resolved, (await DisputeAsync(seed.FindingId))!.Status);
    }

    [Theory]
    [InlineData("CORRECTED", null, null)]        // thiếu số tiền
    [InlineData("KEPT", null, null)]             // thiếu phản hồi
    [InlineData("SOMETHING", null, null)]        // hành động lạ
    public async Task Respond_InvalidBody_Gets400(string action, long? amount, string? response)
    {
        var seed = await SeedAsync();
        await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);
        await AssertError(await Respond(seed.S.LandlordClient, seed.FindingId, new { action, amount, response }),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Respond_CorrectedAboveTheDeposit_Gets400_AmountExceedsDeposit()
    {
        var seed = await SeedAsync();
        await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);
        await AssertError(await Respond(seed.S.LandlordClient, seed.FindingId, new { action = "CORRECTED", amount = Deposit + 1 }),
            HttpStatusCode.BadRequest, "AMOUNT_EXCEEDS_DEPOSIT");
    }

    [Fact]
    public async Task Tenant_CannotRespond_Gets403_AndAnUndisputedItem_Gets409()
    {
        var seed = await SeedAsync();
        await AssertError(await Respond(seed.S.LandlordClient, seed.FindingId, new { action = "KEPT", response = "ok" }),
            HttpStatusCode.Conflict, "INVALID_STATE");
        await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);
        await AssertError(await Respond(seed.S.TenantClient, seed.FindingId, new { action = "KEPT", response = "ok" }),
            HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task AnsweringTwice_Gets409_InvalidState()
    {
        var seed = await SeedAsync();
        await Dispute(seed.S.TenantClient, seed.FindingId, GoodReason);
        await Respond(seed.S.LandlordClient, seed.FindingId, new { action = "KEPT", response = "Giữ nguyên" });
        await AssertError(await Respond(seed.S.LandlordClient, seed.FindingId, new { action = "CORRECTED", amount = 1000 }),
            HttpStatusCode.Conflict, "INVALID_STATE");
    }
}
