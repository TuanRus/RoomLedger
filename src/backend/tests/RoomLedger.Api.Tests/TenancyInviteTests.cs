using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Test tích hợp MD-03 (BR-09 đến BR-14) cùng luồng mời người thuê.
 *
 * @author Tuấn
 * @version 1.0
 */
public class TenancyInviteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateTenancy_IsIdempotentWhileDraft()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var first = await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var tenancy = await first.DataAsync();
        Assert.Equal("DRAFT", tenancy.GetProperty("status").GetString());
        var v1 = tenancy.GetProperty("versions")[0];
        Assert.Equal(1, v1.GetProperty("number").GetInt32());
        Assert.Equal("UPLOADED", v1.GetProperty("status").GetString());
        Assert.Equal("PENDING", v1.GetProperty("aiStatus").GetString());

        var second = await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(tenancy.GetProperty("tenancyId").GetGuid(), (await second.DataAsync()).GetProperty("tenancyId").GetGuid());
    }

    [Fact]
    public async Task Terms_ShowAiValuesAndRecordEditedFields()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var tenancyId = (await (await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null)).DataAsync())
            .GetProperty("tenancyId").GetGuid();
        // Giả lập Lambda read-document (Sang) đã ghi kết quả AI
        await factory.WithDbAsync(async db =>
        {
            var v = await db.TenancyVersions.SingleAsync(x => x.TenancyId == tenancyId);
            v.AiStatus = AiStatus.Succeeded;
            v.AiTermsJson = """{"monthlyRent":{"value":3500000,"confidence":0.95},"deposit":{"value":6000000,"confidence":0.6}}""";
            return await db.SaveChangesAsync();
        });

        var detail = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{tenancyId}")).DataAsync();
        var terms = detail.GetProperty("versions")[0].GetProperty("terms");
        Assert.False(terms.GetProperty("monthlyRent").GetProperty("needsReview").GetBoolean());
        Assert.True(terms.GetProperty("deposit").GetProperty("needsReview").GetBoolean());
        Assert.Equal(6000000, terms.GetProperty("deposit").GetProperty("aiValue").GetInt64());

        var bad = await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/terms",
            new { monthlyRent = 3500000, deposit = 7000000, startDate = "2026-11-01", endDate = "2026-10-01", electricityRate = 3500, waterRate = 20000 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("endDate", (await bad.JsonAsync()).GetProperty("details")[0].GetProperty("field").GetString());

        var ok = await (await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/terms", Scenario.ValidTerms)).DataAsync();
        Assert.Equal("TERMS_REVIEWED", ok.GetProperty("status").GetString());
        Assert.Equal(["deposit", "startDate", "endDate", "electricityRate", "waterRate"],
            ok.GetProperty("editedFields").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Invite_BeforeTermsSaved_ReturnsInvalidState()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var tenancyId = (await (await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null)).DataAsync())
            .GetProperty("tenancyId").GetGuid();
        var res = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/invites", new { email = "a@x.com" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("INVALID_STATE", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task FullContractFlow_InviteAcceptConfirmLocks()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var tenancyId = (await (await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null)).DataAsync())
            .GetProperty("tenancyId").GetGuid();
        await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/terms", Scenario.ValidTerms);
        s.TenancyId = tenancyId;

        var oldToken = await s.InviteAsync(s.Tenant.Email.ToUpperInvariant());
        var token = await s.InviteAsync(s.Tenant.Email);
        var hashes = await factory.WithDbAsync(db => db.Invites.Where(i => i.TenancyId == tenancyId).Select(i => i.TokenHash).ToListAsync());
        Assert.DoesNotContain(token, hashes);

        // Link cũ bị thu hồi khi gửi lại (US-12 AC2)
        Assert.Equal(HttpStatusCode.Gone, (await factory.CreateClient().GetAsync($"/api/v1/invites/{oldToken}")).StatusCode);

        var preview = await (await factory.CreateClient().GetAsync($"/api/v1/invites/{token}")).DataAsync();
        Assert.Equal("P.204", preview.GetProperty("roomCode").GetString());
        Assert.DoesNotContain(s.Tenant.Email, preview.GetProperty("emailMasked").GetString());

        var room = await (await s.LandlordClient.GetAsync($"/api/v1/rooms/{s.RoomId}")).DataAsync();
        Assert.Equal("AWAITING_CONFIRM", room.GetProperty("displayStatus").GetString());

        var stranger = factory.ClientFor(TestUser.Tenant("stranger@example.com"));
        var mismatch = await stranger.PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);
        Assert.Equal("INVITE_EMAIL_MISMATCH", (await mismatch.JsonAsync()).GetProperty("errorCode").GetString());

        // Người khác chưa nhận lời mời thì không thấy hợp đồng
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/tenancies/{tenancyId}")).StatusCode);

        var accept = await s.TenantClient.PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal("AWAITING_TENANT", (await accept.DataAsync()).GetProperty("status").GetString());

        var reuse = await s.TenantClient.PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Gone, reuse.StatusCode);
        Assert.Equal("INVITE_INVALID", (await reuse.JsonAsync()).GetProperty("errorCode").GetString());

        var home = await (await s.TenantClient.GetAsync("/api/v1/tenant/home")).DataAsync();
        Assert.Equal("CONFIRM_CONTRACT", home.GetProperty("todos")[0].GetProperty("type").GetString());

        var confirmed = await (await s.TenantClient.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/confirm",
            new { decision = "CONFIRM" })).DataAsync();
        Assert.Equal("LOCKED", confirmed.GetProperty("status").GetString());
        Assert.Equal("TenancyVersionLocked", factory.Events.Published.Last().DetailType);

        var tenancy = await (await s.TenantClient.GetAsync($"/api/v1/tenancies/{tenancyId}")).DataAsync();
        Assert.Equal("ACTIVE", tenancy.GetProperty("status").GetString());

        var locked = await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/terms", Scenario.ValidTerms);
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal("RECORD_LOCKED", (await locked.JsonAsync()).GetProperty("errorCode").GetString());

        var again = await s.TenantClient.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/confirm", new { decision = "CONFIRM" });
        Assert.Equal("RECORD_LOCKED", (await again.JsonAsync()).GetProperty("errorCode").GetString());

        var audit = await factory.WithDbAsync(db => db.AuditEvents.Where(a => a.EventType == "INVITE_ACCEPTED" || a.EventType == "CONTRACT_CONFIRMED").CountAsync());
        Assert.True(audit >= 2);
    }

    [Fact]
    public async Task Invite_ExpiresAfter72Hours()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var tenancyId = (await (await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null)).DataAsync())
            .GetProperty("tenancyId").GetGuid();
        await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/terms", Scenario.ValidTerms);
        s.TenancyId = tenancyId;
        var token = await s.InviteAsync(s.Tenant.Email);
        await factory.WithDbAsync(db => db.Invites.Where(i => i.TenancyId == tenancyId)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.ExpiresAt, i => i.ExpiresAt.AddHours(-73))));

        var res = await s.TenantClient.PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.Gone, res.StatusCode);
    }

    [Fact]
    public async Task Reject_RequiresReasonAndAllowsReplacementVersion()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var tenancyId = (await (await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null)).DataAsync())
            .GetProperty("tenancyId").GetGuid();
        await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/terms", Scenario.ValidTerms);
        s.TenancyId = tenancyId;
        var token = await s.InviteAsync(s.Tenant.Email);
        await s.TenantClient.PostAsync($"/api/v1/invites/{token}/accept", null);

        var noReason = await s.TenantClient.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/confirm", new { decision = "REJECT" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var rejected = await (await s.TenantClient.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/1/confirm",
            new { decision = "REJECT", reason = "Tiền cọc trên bản giấy là 6 triệu" })).DataAsync();
        Assert.Equal("REJECTED", rejected.GetProperty("status").GetString());

        var v2 = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/versions", new { type = "ADDENDUM" });
        Assert.Equal(HttpStatusCode.Created, v2.StatusCode);
        var v2Data = await v2.DataAsync();
        Assert.Equal(2, v2Data.GetProperty("number").GetInt32());
        Assert.Equal("INITIAL", v2Data.GetProperty("type").GetString());

        // Đã có người thuê nên lưu điều khoản bản thay thế chuyển thẳng sang chờ người thuê xác nhận
        var saved = await (await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/2/terms", Scenario.ValidTerms)).DataAsync();
        Assert.Equal("AWAITING_CONFIRM", saved.GetProperty("status").GetString());
        var locked = await (await s.TenantClient.PostJsonAsync($"/api/v1/tenancies/{tenancyId}/versions/2/confirm", new { decision = "CONFIRM" })).DataAsync();
        Assert.Equal("LOCKED", locked.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Addendum_OnlyWhenActive()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var res = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/versions", new { type = "RENEWAL" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var again = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/versions", new { type = "RENEWAL" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }
}
