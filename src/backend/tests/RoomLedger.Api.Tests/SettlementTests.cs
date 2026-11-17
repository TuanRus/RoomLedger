using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Quyết toán trả phòng (MD-06-FE-05, BR-29; quyết định A2, B3–B5 của Tuấn ngày 17/11).
 * GET /tenancies/{id}/settlement: hai bên xem trước tiền cọc, tiền trừ (mục ACCEPTED/RESOLVED), hóa đơn chưa trả và
 * tiền trả lại, kèm các điều kiện còn thiếu. POST: chỉ chủ nhà, sau khi đã gửi danh sách cho người thuê; thiếu điều
 * kiện → 409 với mã đầu tiên theo thứ tự và details liệt kê tất cả; đủ → ghi bảng settlement, biên bản trả phòng và
 * hợp đồng → SETTLED, phòng → VACANT, audit, phát TenancySettled. Quyết toán hai lần chỉ ghi một lần.
 *
 * @author Sang
 * @version 1.0
 */
public class SettlementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Period = "2026-11";   // kỳ trả phòng: ApiFactory.Clock 2026-11-20
    private const long Deposit = 7_000_000;    // Scenario.ValidTerms

    private sealed record Seed(Scenario S, Guid MoveOutId, Guid AreaId, Guid LandlordId, Guid TenantId);

    /**
     * Trả phòng đã gửi người thuê (AWAITING_CONFIRM): mục ACCEPTED 500.000, RESOLVED 300.000, DISMISSED; chỉ số tháng
     * cuối đã xác nhận; hóa đơn kỳ 10 đã trả 3.800.000 và hóa đơn cuối kỳ 11 SENT 4.200.000.
     * → trừ 800.000, chưa trả 4.200.000, trả lại 2.000.000.
     */
    private async Task<Seed> SeedAsync(bool complete = true)
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var moveIn = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        var areaId = moveIn.GetProperty("areas")[0].GetProperty("areaId").GetGuid();
        await SetStatusAsync(moveIn.GetProperty("inspectionId").GetGuid(), InspectionStatus.Locked);
        var moveOut = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-out", null)).DataAsync();
        var moveOutId = moveOut.GetProperty("inspectionId").GetGuid();
        await SetStatusAsync(moveOutId, InspectionStatus.AwaitingConfirm);
        var ids = await factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == s.TenancyId)
            .Select(t => new { t.LandlordId, TenantId = t.TenantId!.Value }).SingleAsync());
        var seed = new Seed(s, moveOutId, areaId, ids.LandlordId, ids.TenantId);
        if (complete)
        {
            await FindingAsync(seed, FindingStatus.Accepted, 500_000);
            await FindingAsync(seed, FindingStatus.Resolved, 300_000);
            await FindingAsync(seed, FindingStatus.Dismissed, null);
            await InvoiceAsync(seed, "2026-10", InvoiceStatus.Paid, 3_800_000);
            await InvoiceAsync(seed, Period, InvoiceStatus.Sent, 4_200_000);
        }
        return seed;
    }

    private Task SetStatusAsync(Guid id, InspectionStatus status) =>
        factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == id).ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, status)));

    private Task<Guid> FindingAsync(Seed seed, FindingStatus status, long? amount) => factory.WithDbAsync(async db =>
    {
        var f = new Finding { InspectionId = seed.MoveOutId, RoomAreaId = seed.AreaId, Title = "Vết bẩn", Status = status, Amount = amount };
        db.Findings.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    });

    /** Chỉ số điện, nước của kỳ (tạo nếu chưa có) với trạng thái cho trước; trả id (điện, nước). */
    private Task<(Guid Electricity, Guid Water)> ReadingsAsync(Seed seed, string period,
        MeterReadingStatus electricity = MeterReadingStatus.Confirmed, MeterReadingStatus water = MeterReadingStatus.Confirmed) =>
        factory.WithDbAsync(async db =>
        {
            Guid Get(MeterKind kind, MeterReadingStatus status)
            {
                var existing = db.MeterReadings.SingleOrDefault(m => m.TenancyId == seed.S.TenancyId && m.Period == period && m.Kind == kind);
                if (existing is not null)
                {
                    existing.Status = status;
                    return existing.Id;
                }
                var m = new MeterReading
                {
                    RoomId = seed.S.RoomId, TenancyId = seed.S.TenancyId, Period = period, Kind = kind, Status = status,
                    Value = 100, PhotoStatus = FileStatus.Ready, PhotoKey = $"meters/{Guid.NewGuid():N}.jpg",
                };
                db.MeterReadings.Add(m);
                return m.Id;
            }
            var ids = (Get(MeterKind.Electricity, electricity), Get(MeterKind.Water, water));
            await db.SaveChangesAsync();
            return ids;
        });

    private async Task<Guid> InvoiceAsync(Seed seed, string period, InvoiceStatus status, long total)
    {
        var (e, w) = await ReadingsAsync(seed, period);
        return await factory.WithDbAsync(async db =>
        {
            var inv = new Invoice
            {
                TenancyId = seed.S.TenancyId, Period = period, Status = status, RoomRent = 3_500_000, ServiceFee = 0,
                ElectricityReadingId = e, WaterReadingId = w, Total = total,
            };
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();
            return inv.Id;
        });
    }

    private static string Url(Seed seed) => $"/api/v1/tenancies/{seed.S.TenancyId}/settlement";

    private int SettledEvents(Guid tenancyId) =>
        factory.Events.Published.Count(e => e.DetailType == "TenancySettled" && e.Detail.GetProperty("tenancyId").GetGuid() == tenancyId);

    private async Task<(InspectionStatus Inspection, TenancyStatus Tenancy, RoomStatus Room, int Settlements)> StateAsync(Seed seed) =>
        await factory.WithDbAsync(async db => (
            await db.Inspections.Where(i => i.Id == seed.MoveOutId).Select(i => i.Status).SingleAsync(),
            await db.Tenancies.Where(t => t.Id == seed.S.TenancyId).Select(t => t.Status).SingleAsync(),
            await db.Rooms.Where(r => r.Id == seed.S.RoomId).Select(r => r.Status).SingleAsync(),
            await db.Settlements.CountAsync(x => x.TenancyId == seed.S.TenancyId)));

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    private static void AssertFigures(JsonElement data, long deductions, long unpaid, long refund)
    {
        Assert.Equal(Deposit, data.GetProperty("deposit").GetInt64());
        Assert.Equal(deductions, data.GetProperty("deductionsTotal").GetInt64());
        Assert.Equal(unpaid, data.GetProperty("unpaidInvoicesTotal").GetInt64());
        Assert.Equal(refund, data.GetProperty("refund").GetInt64());
    }

    // ---------- Xem trước ----------

    [Fact]
    public async Task Preview_BothPartiesSeeTheFigures_AndThatItCanBeSettled()
    {
        var seed = await SeedAsync();

        foreach (var client in new[] { seed.S.LandlordClient, seed.S.TenantClient })
        {
            var res = await client.GetAsync(Url(seed));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var data = await res.DataAsync();
            Assert.Equal("PENDING", data.GetProperty("status").GetString());
            Assert.Equal(Period, data.GetProperty("finalPeriod").GetString());
            Assert.Equal(seed.MoveOutId, data.GetProperty("inspectionId").GetGuid());
            AssertFigures(data, 800_000, 4_200_000, 2_000_000);
            Assert.True(data.GetProperty("canSettle").GetBoolean());
            Assert.Empty(data.GetProperty("blockers").EnumerateArray());
        }
        Assert.Equal(0, (await StateAsync(seed)).Settlements);   // xem trước không ghi gì
    }

    [Fact]
    public async Task Preview_ListsEveryUnmetCondition_InOrder()
    {
        var seed = await SeedAsync(complete: false);
        await ReadingsAsync(seed, Period, water: MeterReadingStatus.PendingConfirm);
        await FindingAsync(seed, FindingStatus.Disputed, 200_000);
        await FindingAsync(seed, FindingStatus.Charged, 100_000);

        var data = await (await seed.S.LandlordClient.GetAsync(Url(seed))).DataAsync();

        Assert.False(data.GetProperty("canSettle").GetBoolean());
        Assert.Equal(["FINAL_READING_UNCONFIRMED", "FINAL_INVOICE_MISSING", "OPEN_DISPUTES", "PENDING_FINDINGS"],
            data.GetProperty("blockers").EnumerateArray().Select(b => b.GetProperty("code").GetString()));
    }

    // ---------- Quyết toán ----------

    [Fact]
    public async Task LandlordSettles_EverythingIsClosed_AndTheTenantIsNotified()
    {
        var seed = await SeedAsync();

        var res = await seed.S.LandlordClient.PostAsync(Url(seed), null);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var data = await res.DataAsync();
        Assert.Equal("SETTLED", data.GetProperty("status").GetString());
        AssertFigures(data, 800_000, 4_200_000, 2_000_000);
        Assert.Equal(seed.LandlordId, data.GetProperty("settledBy").GetGuid());
        Assert.Equal(factory.Clock.GetUtcNow(), data.GetProperty("settledAt").GetDateTimeOffset());

        Assert.Equal((InspectionStatus.Settled, TenancyStatus.Settled, RoomStatus.Vacant, 1), await StateAsync(seed));
        var row = await factory.WithDbAsync(db => db.Settlements.AsNoTracking().SingleAsync(x => x.TenancyId == seed.S.TenancyId));
        Assert.Equal((Deposit, 800_000L, 4_200_000L, 2_000_000L, seed.LandlordId),
            (row.Deposit, row.DeductionsTotal, row.UnpaidInvoicesTotal, row.Refund, row.SettledBy));
        Assert.Null(row.PdfKey);
        Assert.Equal(data.GetProperty("settlementId").GetGuid(), row.Id);

        Assert.Equal(1, await factory.WithDbAsync(db =>
            db.AuditEvents.CountAsync(a => a.TargetId == seed.S.TenancyId && a.EventType == "TENANCY_SETTLED")));
        Assert.Equal(1, SettledEvents(seed.S.TenancyId));
        var detail = factory.Events.Last("TenancySettled");
        Assert.Equal(row.Id, detail.GetProperty("settlementId").GetGuid());
        Assert.Equal(seed.MoveOutId, detail.GetProperty("inspectionId").GetGuid());
        Assert.Equal(seed.TenantId, detail.GetProperty("notifyUserId").GetGuid());
        Assert.Equal(2_000_000, detail.GetProperty("refund").GetInt64());
    }

    [Fact]
    public async Task UnpaidInvoicesAboveTheDeposit_RefundIsNegative()
    {
        var seed = await SeedAsync(complete: false);
        await FindingAsync(seed, FindingStatus.Accepted, 800_000);
        await InvoiceAsync(seed, "2026-10", InvoiceStatus.Sent, 4_800_000);
        await InvoiceAsync(seed, Period, InvoiceStatus.Draft, 4_200_000);   // DRAFT cũng là chưa trả

        var data = await (await seed.S.LandlordClient.PostAsync(Url(seed), null)).DataAsync();

        AssertFigures(data, 800_000, 9_000_000, -2_800_000);
    }

    [Fact]
    public async Task UnmetConditions_Gets409_FirstCodeInOrder_DetailsListAll_NothingChanges()
    {
        var seed = await SeedAsync(complete: false);
        await ReadingsAsync(seed, Period, electricity: MeterReadingStatus.NeedsReview);
        await InvoiceAsync(seed, "2026-10", InvoiceStatus.Disputed, 3_800_000);
        await FindingAsync(seed, FindingStatus.Charged, 100_000);

        var res = await seed.S.LandlordClient.PostAsync(Url(seed), null);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.Equal("FINAL_READING_UNCONFIRMED", body.GetProperty("errorCode").GetString());
        Assert.Equal(["FINAL_READING_UNCONFIRMED", "FINAL_INVOICE_MISSING", "OPEN_DISPUTES", "PENDING_FINDINGS"],
            body.GetProperty("details").EnumerateArray().Select(b => b.GetProperty("code").GetString()));
        Assert.Equal((InspectionStatus.AwaitingConfirm, TenancyStatus.MovingOut, RoomStatus.Occupied, 0), await StateAsync(seed));
        Assert.Equal(0, SettledEvents(seed.S.TenancyId));
    }

    [Fact]
    public async Task OnlyAnOpenFindingDispute_Gets409_OpenDisputes()
    {
        var seed = await SeedAsync();
        await FindingAsync(seed, FindingStatus.Disputed, 200_000);

        await AssertError(await seed.S.LandlordClient.PostAsync(Url(seed), null), HttpStatusCode.Conflict, "OPEN_DISPUTES");
    }

    [Fact]
    public async Task SettledTwice_Returns200_TheSameSettlement_WrittenOnce()
    {
        var seed = await SeedAsync();
        var first = await (await seed.S.LandlordClient.PostAsync(Url(seed), null)).DataAsync();

        var again = await seed.S.LandlordClient.PostAsync(Url(seed), null);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first.GetProperty("settlementId").GetGuid(), (await again.DataAsync()).GetProperty("settlementId").GetGuid());
        Assert.Equal(1, (await StateAsync(seed)).Settlements);
        Assert.Equal(1, SettledEvents(seed.S.TenancyId));
    }

    [Fact]
    public async Task TwoSettlesAtOnce_WriteOnce()
    {
        var seed = await SeedAsync();

        var results = await Task.WhenAll(
            seed.S.LandlordClient.PostAsync(Url(seed), null),
            seed.S.LandlordClient.PostAsync(Url(seed), null));

        Assert.All(results, r => Assert.True(r.IsSuccessStatusCode));
        Assert.Equal(1, (await StateAsync(seed)).Settlements);
        Assert.Equal(1, SettledEvents(seed.S.TenancyId));
    }

    [Fact]
    public async Task AfterSettlement_TheSettledFiguresAreShown_AndItemsCanNoLongerChange()
    {
        var seed = await SeedAsync();
        var dismissed = await FindingAsync(seed, FindingStatus.Dismissed, null);
        await seed.S.LandlordClient.PostAsync(Url(seed), null);
        await InvoiceAsync(seed, "2026-12", InvoiceStatus.Sent, 1_000_000);   // phát sinh sau: không đổi số đã chốt

        var data = await (await seed.S.TenantClient.GetAsync(Url(seed))).DataAsync();

        Assert.Equal("SETTLED", data.GetProperty("status").GetString());
        AssertFigures(data, 800_000, 4_200_000, 2_000_000);
        await AssertError(await seed.S.TenantClient.PostAsync($"/api/v1/findings/{dismissed}/accept", null), HttpStatusCode.Conflict, "RECORD_LOCKED");
    }

    // ---------- Quyền và trạng thái ----------

    [Fact]
    public async Task Tenant_CannotSettle_Gets403_Stranger_Gets404()
    {
        var seed = await SeedAsync();
        var stranger = factory.ClientFor(TestUser.Landlord($"stranger-{Guid.NewGuid():N}@example.com"));

        await AssertError(await seed.S.TenantClient.PostAsync(Url(seed), null), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertError(await stranger.GetAsync(Url(seed)), HttpStatusCode.NotFound, "NOT_FOUND");
        await AssertError(await stranger.PostAsync(Url(seed), null), HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Equal(0, (await StateAsync(seed)).Settlements);
    }

    [Theory]
    [InlineData(InspectionStatus.Open)]
    [InlineData(InspectionStatus.Comparing)]
    [InlineData(InspectionStatus.Reviewing)]
    public async Task NotYetSentToTheTenant_Gets409_InvalidState(InspectionStatus status)
    {
        var seed = await SeedAsync();
        await SetStatusAsync(seed.MoveOutId, status);

        await AssertError(await seed.S.LandlordClient.GetAsync(Url(seed)), HttpStatusCode.Conflict, "INVALID_STATE");
        await AssertError(await seed.S.LandlordClient.PostAsync(Url(seed), null), HttpStatusCode.Conflict, "INVALID_STATE");
    }

    [Fact]
    public async Task NoMoveOutYet_Gets409_InvalidState()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();

        await AssertError(await s.LandlordClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/settlement", null),
            HttpStatusCode.Conflict, "INVALID_STATE");
    }
}
