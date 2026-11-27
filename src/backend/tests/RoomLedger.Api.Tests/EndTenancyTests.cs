using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Test tích hợp luồng kết thúc hợp đồng và chuyển phòng (LL-10).
 * ApiFactory.Clock là 2026-11-20; hợp đồng của Scenario bắt đầu 2026-11-01, kỳ cuối là 2026-11.
 *
 * @author Tuấn
 * @version 1.0
 */
public class EndTenancyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string EndDate = "2026-11-25";
    private const string FinalPeriod = "2026-11";

    [Fact]
    public async Task Start_Update_Cancel_RoundTrip()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var start = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = EndDate, kind = "LEAVE" });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var data = await start.DataAsync();
        Assert.Equal(FinalPeriod, data.GetProperty("finalPeriod").GetString());
        Assert.Equal("LEAVE", data.GetProperty("kind").GetString());
        Assert.Equal("Người thuê B", data.GetProperty("tenantName").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("electricity").GetProperty("readingId").ValueKind);
        Assert.Equal(3500, data.GetProperty("rates").GetProperty("electricity").GetInt64());

        var room = await (await s.LandlordClient.GetAsync($"/api/v1/rooms/{s.RoomId}")).DataAsync();
        Assert.Equal("MOVING_OUT", room.GetProperty("displayStatus").GetString());

        var update = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = "2026-11-28", kind = "LEAVE" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("2026-11-28", (await update.DataAsync()).GetProperty("endDate").GetString());

        var cancel = await s.LandlordClient.DeleteAsync($"/api/v1/tenancies/{s.TenancyId}/end");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var tenancy = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{s.TenancyId}")).DataAsync();
        Assert.Equal("ACTIVE", tenancy.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{s.TenancyId}/end")).StatusCode);
        Assert.Equal("INVALID_STATE", (await (await s.LandlordClient.DeleteAsync($"/api/v1/tenancies/{s.TenancyId}/end")).JsonAsync())
            .GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Start_ValidatesDateKindAndTarget()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var url = $"/api/v1/tenancies/{s.TenancyId}/end";

        Assert.Equal(HttpStatusCode.BadRequest, (await s.LandlordClient.PostJsonAsync(url, new { kind = "LEAVE" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.LandlordClient.PostJsonAsync(url, new { endDate = "2026-10-31", kind = "LEAVE" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.LandlordClient.PostJsonAsync(url, new { endDate = EndDate, kind = "MOVE" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await s.LandlordClient.PostJsonAsync(url, new { endDate = EndDate, kind = "MOVE", targetRoomId = s.RoomId })).StatusCode);

        // Phòng đích đã có hợp đồng (kể cả nháp) không còn trống
        var busy = await AddRoomAsync(s, "P.301");
        Assert.Equal(HttpStatusCode.Created, (await s.LandlordClient.PostAsync($"/api/v1/rooms/{busy}/tenancies", null)).StatusCode);
        var occupied = await s.LandlordClient.PostJsonAsync(url, new { endDate = EndDate, kind = "MOVE", targetRoomId = busy });
        Assert.Equal(HttpStatusCode.Conflict, occupied.StatusCode);
        Assert.Equal("ROOM_OCCUPIED", (await occupied.JsonAsync()).GetProperty("errorCode").GetString());

        // Phòng của chủ nhà khác coi như không tồn tại
        var other = await new Scenario(factory).WithRoomAsync("X.1");
        var foreign = await s.LandlordClient.PostJsonAsync(url, new { endDate = EndDate, kind = "MOVE", targetRoomId = other.RoomId });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // Chưa bắt đầu thành công lần nào: hợp đồng vẫn ACTIVE
        var tenancy = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{s.TenancyId}")).DataAsync();
        Assert.Equal("ACTIVE", tenancy.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Finish_WithoutConfirmedReadings_Returns409FinalReadingMissing()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await StartLeaveAsync(s);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);

        var res = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish", new { lastRent = 2_000_000 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var json = await res.JsonAsync();
        Assert.Equal("FINAL_READING_MISSING", json.GetProperty("errorCode").GetString());
        var details = json.GetProperty("details");
        Assert.Single(details.EnumerateArray());
        Assert.Equal("water", details[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task FinishLeave_CreatesDraftFinalInvoice_SettlesTenancyAndFreesRoom()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await SetOpeningAsync(s.TenancyId, 900, 40);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-10" })).DataAsync();
        var oldInvoice = gen.GetProperty("created")[0].GetProperty("invoiceId").GetGuid();
        await s.LandlordClient.PostAsync($"/api/v1/invoices/{oldInvoice}/send", null);

        await StartLeaveAsync(s);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 56);

        var preview = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{s.TenancyId}/end")).DataAsync();
        // 3.500.000 + 100 kWh × 3.500 + 10 m³ × 20.000 + 100.000 phí dịch vụ
        Assert.Equal(4_150_000, preview.GetProperty("unpaidPrevious").GetInt64());
        Assert.Equal(1000, preview.GetProperty("electricity").GetProperty("previous").GetInt64());
        Assert.Equal(1120, preview.GetProperty("electricity").GetProperty("value").GetInt64());
        Assert.Equal("CONFIRMED", preview.GetProperty("water").GetProperty("status").GetString());

        var finish = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish", new { lastRent = 2_000_000 });
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        var data = await finish.DataAsync();
        Assert.Equal(2_000_000, data.GetProperty("lastRent").GetInt64());
        Assert.Equal(4_150_000, data.GetProperty("unpaidPrevious").GetInt64());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("endedAt").ValueKind);

        var invoiceId = data.GetProperty("finalInvoiceId").GetGuid();
        var invoice = await (await s.LandlordClient.GetAsync($"/api/v1/invoices/{invoiceId}")).DataAsync();
        Assert.Equal("DRAFT", invoice.GetProperty("status").GetString());
        Assert.Equal(FinalPeriod, invoice.GetProperty("period").GetString());
        Assert.Equal(2_000_000, invoice.GetProperty("roomRent").GetInt64());
        Assert.Equal(120, invoice.GetProperty("electricity").GetProperty("usage").GetInt64());
        Assert.Equal(6, invoice.GetProperty("water").GetProperty("usage").GetInt64());
        // 2.000.000 + 120 kWh × 3.500 + 6 m³ × 20.000 + 100.000; hóa đơn cũ không gộp vào
        Assert.Equal(2_640_000, invoice.GetProperty("total").GetInt64());

        var tenancy = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{s.TenancyId}")).DataAsync();
        Assert.Equal("SETTLED", tenancy.GetProperty("status").GetString());
        var room = await (await s.LandlordClient.GetAsync($"/api/v1/rooms/{s.RoomId}")).DataAsync();
        Assert.Equal("VACANT", room.GetProperty("displayStatus").GetString());
        Assert.Equal("VACANT", room.GetProperty("status").GetString());

        var again = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish", new { lastRent = 2_000_000 });
        Assert.Equal("INVALID_STATE", (await again.JsonAsync()).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PostAsync($"/api/v1/invoices/{invoiceId}/send", null)).StatusCode);
    }

    [Fact]
    public async Task Finish_WhenPeriodAlreadyInvoiced_Returns409AlreadyInvoiced()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        await StartLeaveAsync(s);
        var elec = await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        var water = await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 56);
        await factory.WithDbAsync(async db =>
        {
            db.Invoices.Add(new Invoice { TenancyId = s.TenancyId, Period = FinalPeriod, ElectricityReadingId = elec, WaterReadingId = water });
            return await db.SaveChangesAsync();
        });

        var res = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish", new { lastRent = 1_000_000 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("ALREADY_INVOICED", (await res.JsonAsync()).GetProperty("errorCode").GetString());
        var tenancy = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{s.TenancyId}")).DataAsync();
        Assert.Equal("MOVING_OUT", tenancy.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Generate_SkipsEndingTenancyForItsFinalPeriod()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        await StartLeaveAsync(s);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 56);

        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = FinalPeriod })).DataAsync();
        Assert.Empty(gen.GetProperty("created").EnumerateArray());
        Assert.Equal("ENDING_TENANCY", gen.GetProperty("skipped")[0].GetProperty("reason").GetString());

        // Kỳ trước kỳ cuối vẫn lập bình thường
        var earlier = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-10" })).DataAsync();
        Assert.NotEqual("ENDING_TENANCY", earlier.GetProperty("skipped")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task FinishMove_CreatesAwaitingTenancyInTargetRoom_TenantCanConfirm()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var target = await AddRoomAsync(s, "P.305");
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);

        var start = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end",
            new { endDate = EndDate, kind = "MOVE", targetRoomId = target });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var targetRoom = (await start.DataAsync()).GetProperty("targetRoom");
        Assert.Equal("P.305", targetRoom.GetProperty("code").GetString());
        Assert.Equal(3_500_000, targetRoom.GetProperty("monthlyRent").GetInt64());

        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 56);

        var noOpening = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish", new { lastRent = 2_000_000, startElectricity = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, noOpening.StatusCode);

        var finish = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish",
            new { lastRent = 2_000_000, startElectricity = 10, startWater = 2 });
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        var newId = (await finish.DataAsync()).GetProperty("newTenancyId").GetGuid();

        var moved = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{newId}")).DataAsync();
        Assert.Equal("AWAITING_TENANT", moved.GetProperty("status").GetString());
        Assert.Equal(target, moved.GetProperty("roomId").GetGuid());
        var terms = moved.GetProperty("versions")[0].GetProperty("terms");
        Assert.Equal(3_500_000, terms.GetProperty("monthlyRent").GetProperty("value").GetInt64());
        Assert.Equal(7_000_000, terms.GetProperty("deposit").GetProperty("value").GetInt64());
        Assert.Equal(EndDate, terms.GetProperty("startDate").GetProperty("value").GetString());
        Assert.Equal("AWAITING_CONFIRM", moved.GetProperty("versions")[0].GetProperty("status").GetString());

        await factory.WithDbAsync(async db =>
        {
            var t = await db.Tenancies.AsNoTracking().FirstAsync(x => x.Id == newId);
            Assert.Equal(10, t.OpeningElectricity);
            Assert.Equal(2, t.OpeningWater);
            Assert.Equal(s.Tenant.Id, t.TenantId);
            return 0;
        });

        var confirm = await s.TenantClient.PostJsonAsync($"/api/v1/tenancies/{newId}/versions/1/confirm", new { decision = "CONFIRM" });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var active = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{newId}")).DataAsync();
        Assert.Equal("ACTIVE", active.GetProperty("status").GetString());
        var room = await (await s.LandlordClient.GetAsync($"/api/v1/rooms/{target}")).DataAsync();
        Assert.Equal("OCCUPIED", room.GetProperty("status").GetString());
        var old = await (await s.LandlordClient.GetAsync($"/api/v1/rooms/{s.RoomId}")).DataAsync();
        Assert.Equal("VACANT", old.GetProperty("displayStatus").GetString());
    }

    [Fact]
    public async Task Routes_TenantForbidden_OtherLandlordNotFound()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await StartLeaveAsync(s);
        var url = $"/api/v1/tenancies/{s.TenancyId}/end";

        Assert.Equal(HttpStatusCode.Forbidden, (await s.TenantClient.PostJsonAsync(url, new { endDate = EndDate, kind = "LEAVE" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.TenantClient.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.TenantClient.PostJsonAsync($"{url}:finish", new { lastRent = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.TenantClient.DeleteAsync(url)).StatusCode);

        var stranger = await new Scenario(factory).WithRoomAsync("Z.1");
        var client = stranger.LandlordClient;
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostJsonAsync(url, new { endDate = EndDate, kind = "LEAVE" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostJsonAsync($"{url}:finish", new { lastRent = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(url)).StatusCode);
    }

    [Fact]
    public async Task End_WithLegacyMoveOutInspection_Returns409()
    {
        var withInspection = await new Scenario(factory).WithActiveTenancyAsync();
        await factory.WithDbAsync(async db =>
        {
            db.Inspections.Add(new Inspection { TenancyId = withInspection.TenancyId, Type = InspectionType.MoveOut });
            return await db.SaveChangesAsync();
        });
        var blocked = await withInspection.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{withInspection.TenancyId}/end", new { endDate = EndDate, kind = "LEAVE" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("INVALID_STATE", (await blocked.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Cancel_AfterFinish_Returns409AndKeepsSettled()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        await StartLeaveAsync(s);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 56);
        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish", new { lastRent = 1_000_000 })).StatusCode);

        var cancel = await s.LandlordClient.DeleteAsync($"/api/v1/tenancies/{s.TenancyId}/end");
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        var update = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = "2026-11-26", kind = "LEAVE" });
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        var tenancy = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{s.TenancyId}")).DataAsync();
        Assert.Equal("SETTLED", tenancy.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Start_RejectsPeriodAlreadyInvoiced()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await SetOpeningAsync(s.TenancyId, 900, 40);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 50);
        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = FinalPeriod })).DataAsync();
        Assert.Single(gen.GetProperty("created").EnumerateArray());

        var res = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = EndDate, kind = "LEAVE" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var json = await res.JsonAsync();
        Assert.Equal("ALREADY_INVOICED", json.GetProperty("errorCode").GetString());
        Assert.Contains("chọn ngày kết thúc ở kỳ sau", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Cancel_DeletesUnconfirmedFinalReadings_KeepsConfirmed_AndPeriodChangeIsBlocked()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await StartLeaveAsync(s);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        var pending = await s.LandlordClient.PostJsonAsync($"/api/v1/rooms/{s.RoomId}/meter-readings",
            new { kind = "WATER", period = FinalPeriod, fileName = "a.jpg", contentType = "image/jpeg", size = 10, sha256 = Scenario.Sha });
        Assert.True(pending.IsSuccessStatusCode);

        var moved = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = "2026-12-05", kind = "LEAVE" });
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal("INVALID_STATE", (await moved.JsonAsync()).GetProperty("errorCode").GetString());

        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.DeleteAsync($"/api/v1/tenancies/{s.TenancyId}/end")).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var readings = await db.MeterReadings.Where(m => m.TenancyId == s.TenancyId).ToListAsync();
            Assert.Single(readings);
            Assert.Equal(MeterKind.Electricity, readings[0].Kind);
            return 0;
        });
    }

    [Fact]
    public async Task SameMonthRelet_NewTenantHasOwnReadings_GenerateBillsNewTenant()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        var start = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = "2026-11-10", kind = "LEAVE" });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 56);
        var oldTenancy = s.TenancyId;
        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{oldTenancy}/end:finish", new { lastRent = 1_000_000 })).StatusCode);

        // Người thuê B thuê lại đúng phòng đó trong cùng tháng
        var tenantB = TestUser.Tenant($"tenant-b-{Guid.NewGuid():N}@example.com");
        var bClient = factory.ClientFor(tenantB);
        var created = await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        s.TenancyId = (await created.DataAsync()).GetProperty("tenancyId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{s.TenancyId}/versions/1/terms", Scenario.ValidTerms)).StatusCode);
        var token = await s.InviteAsync(tenantB.Email);
        Assert.Equal(HttpStatusCode.OK, (await bClient.PostAsync($"/api/v1/invites/{token}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/versions/1/confirm", new { decision = "CONFIRM" })).StatusCode);
        await SetOpeningAsync(s.TenancyId, 1120, 56);

        // Chỉ số cùng kỳ 2026-11 của B không đụng chỉ số cuối của A
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1150);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 58);
        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = FinalPeriod })).DataAsync();
        Assert.Empty(gen.GetProperty("skipped").EnumerateArray());
        // 3.500.000 + 30 kWh × 3.500 + 2 m³ × 20.000 + 100.000
        Assert.Equal(3_745_000, gen.GetProperty("created")[0].GetProperty("total").GetInt64());

        // Hóa đơn cuối của A giữ nguyên số của A
        var old = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{oldTenancy}/end")).DataAsync();
        Assert.Equal(1120, old.GetProperty("electricity").GetProperty("value").GetInt64());
        Assert.Equal(1000, old.GetProperty("electricity").GetProperty("previous").GetInt64());
    }

    [Fact]
    public async Task Generate_SkipsEndingTenancyForPeriodsAfterFinalPeriod()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await SetOpeningAsync(s.TenancyId, 900, 40);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 50);
        await factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == s.TenancyId).ExecuteUpdateAsync(u => u
            .SetProperty(t => t.Status, TenancyStatus.MovingOut)
            .SetProperty(t => t.EndDate, (DateOnly?)new DateOnly(2026, 10, 15))
            .SetProperty(t => t.EndKind, (EndKind?)EndKind.Leave)));
        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = FinalPeriod })).DataAsync();
        Assert.Empty(gen.GetProperty("created").EnumerateArray());
        Assert.Equal("ENDING_TENANCY", gen.GetProperty("skipped")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AfterLeave_TenantHomeStillShowsFinalInvoiceAndOldDebt_AndDepositIsReturned()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await SetOpeningAsync(s.TenancyId, 900, 40);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-10" })).DataAsync();
        var oldInvoice = gen.GetProperty("created")[0].GetProperty("invoiceId").GetGuid();
        await s.LandlordClient.PostAsync($"/api/v1/invoices/{oldInvoice}/send", null);
        await StartLeaveAsync(s);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 56);
        var finish = await (await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish", new { lastRent = 2_000_000 })).DataAsync();
        Assert.Equal(7_000_000, finish.GetProperty("deposit").GetInt64());
        var finalInvoice = finish.GetProperty("finalInvoiceId").GetGuid();
        await s.LandlordClient.PostAsync($"/api/v1/invoices/{finalInvoice}/send", null);

        var home = await (await s.TenantClient.GetAsync("/api/v1/tenant/home")).DataAsync();
        var pay = home.GetProperty("todos").EnumerateArray().Where(t => t.GetProperty("type").GetString() == "PAY_INVOICE").ToList();
        Assert.Equal(2, pay.Count);
        Assert.Equal(FinalPeriod, home.GetProperty("latestInvoice").GetProperty("period").GetString());
    }

    [Fact]
    public async Task FinishMove_PublishesTenancyMovedEventAfterCommit()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var target = await AddRoomAsync(s, "P.309");
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = EndDate, kind = "MOVE", targetRoomId = target });
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Electricity, 1120);
        await s.ConfirmedReadingAsync(FinalPeriod, MeterKind.Water, 56);
        var finish = await (await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish",
            new { lastRent = 1_000_000, startElectricity = 10, startWater = 2 })).DataAsync();

        var e = factory.Events.Last("TenancyMoved");
        Assert.Equal(finish.GetProperty("newTenancyId").GetGuid(), e.GetProperty("newTenancyId").GetGuid());
        Assert.Equal(s.Tenant.Email, e.GetProperty("tenantEmail").GetString());
        Assert.Equal("P.309", e.GetProperty("roomCode").GetString());
        Assert.Equal("Nhà trọ Hoa Sen", e.GetProperty("propertyName").GetString());
    }

    [Fact]
    public async Task SameMonthRelet_NextPeriodUsesNewTenantsOwnPreviousReading()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        await factory.WithDbAsync(db => db.TenancyVersions.Where(v => v.TenancyId == s.TenancyId)
            .ExecuteUpdateAsync(u => u.SetProperty(v => v.StartDate, (DateOnly?)new DateOnly(2026, 9, 1))));
        await s.ConfirmedReadingAsync("2026-09", MeterKind.Electricity, 900);
        await s.ConfirmedReadingAsync("2026-09", MeterKind.Water, 40);
        Assert.Equal(HttpStatusCode.Created, (await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = "2026-10-10", kind = "LEAVE" })).StatusCode);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1000);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 50);
        Assert.Equal(HttpStatusCode.OK, (await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end:finish", new { lastRent = 1_000_000 })).StatusCode);

        var tenantB = TestUser.Tenant($"tenant-b-{Guid.NewGuid():N}@example.com");
        var bClient = factory.ClientFor(tenantB);
        var created = await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null);
        s.TenancyId = (await created.DataAsync()).GetProperty("tenancyId").GetGuid();
        await s.LandlordClient.PatchJsonAsync($"/api/v1/tenancies/{s.TenancyId}/versions/1/terms", Scenario.ValidTerms);
        var token = await s.InviteAsync(tenantB.Email);
        await bClient.PostAsync($"/api/v1/invites/{token}/accept", null);
        await bClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/versions/1/confirm", new { decision = "CONFIRM" });
        await SetOpeningAsync(s.TenancyId, 1000, 50);

        // B có chỉ số cùng kỳ 2026-10 (sau chỉ số cuối của A) và kỳ 2026-11
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Electricity, 1030);
        await s.ConfirmedReadingAsync("2026-10", MeterKind.Water, 52);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Electricity, 1100);
        await s.ConfirmedReadingAsync("2026-11", MeterKind.Water, 55);

        var gen = await (await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/invoices:generate", new { period = "2026-11" })).DataAsync();
        Assert.Empty(gen.GetProperty("skipped").EnumerateArray());
        // Chỉ dùng 2026-11 − 2026-10 của B: 3.500.000 + 70 kWh × 3.500 + 3 m³ × 20.000 + 100.000
        Assert.Equal(3_905_000, gen.GetProperty("created")[0].GetProperty("total").GetInt64());
    }

    private static async Task StartLeaveAsync(Scenario s)
    {
        var res = await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/end", new { endDate = EndDate, kind = "LEAVE" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    private static async Task<Guid> AddRoomAsync(Scenario s, string code)
    {
        var res = await s.LandlordClient.PostJsonAsync($"/api/v1/properties/{s.PropertyId}/rooms",
            new { code, monthlyRent = 3500000, areas = new[] { new { name = "Phòng ngủ", minPhotos = 4 } } });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.DataAsync()).GetProperty("roomId").GetGuid();
    }

    private Task SetOpeningAsync(Guid tenancyId, long? electricity, long? water) =>
        factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == tenancyId)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.OpeningElectricity, electricity).SetProperty(t => t.OpeningWater, water)));
}
