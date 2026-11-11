using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Chỉ số điện nước ban đầu trong biên bản nhận phòng (MD-04-FE-04, BR-18 RULE 5).
 * PUT /inspections/{id}/opening-readings: hai bên của hợp đồng nhập khi biên bản nhận phòng chưa khóa; ghi vào
 * tenancy.opening_electricity / opening_water (cột của Tuấn, MD-05 dùng làm chỉ số cũ cho hóa đơn đầu tiên).
 * Đổi số sau khi đã có xác nhận → hủy xác nhận, biên bản về OPEN. Đã khóa → 409 RECORD_LOCKED.
 * Xác nhận biên bản cần đủ hai chỉ số (test ở InspectionConfirmTests).
 *
 * @author Sang
 * @version 1.0
 */
public class OpeningReadingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Seed(Scenario S, Guid InspectionId);

    private async Task<Seed> SeedAsync()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var opened = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        return new Seed(s, opened.GetProperty("inspectionId").GetGuid());
    }

    private static Task<HttpResponseMessage> Put(HttpClient c, Guid inspectionId, object body) =>
        c.PutAsJsonAsync($"/api/v1/inspections/{inspectionId}/opening-readings", body);

    private Task<(long? Electricity, long? Water)> ReadingsAsync(Guid tenancyId) =>
        factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == tenancyId)
            .Select(t => new ValueTuple<long?, long?>(t.OpeningElectricity, t.OpeningWater)).SingleAsync());

    private Task SetInspectionAsync(Guid id, InspectionStatus status, DateTimeOffset? landlordConfirmedAt = null) =>
        factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == id).ExecuteUpdateAsync(x => x
            .SetProperty(i => i.Status, status).SetProperty(i => i.LandlordConfirmedAt, landlordConfirmedAt)));

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Party_SetsOpeningReadings_StoredOnTheTenancy_AndShownOnTheInspection()
    {
        var seed = await SeedAsync();

        var res = await Put(seed.S.LandlordClient, seed.InspectionId, new { electricity = 1234, water = 56 });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal((1234L, 56L), await ReadingsAsync(seed.S.TenancyId));
        var view = await (await seed.S.TenantClient.GetAsync($"/api/v1/inspections/{seed.InspectionId}")).DataAsync();
        Assert.Equal(1234, view.GetProperty("openingElectricity").GetInt64());
        Assert.Equal(56, view.GetProperty("openingWater").GetInt64());
        Assert.Equal(1, await factory.WithDbAsync(db =>
            db.AuditEvents.CountAsync(a => a.TargetId == seed.S.TenancyId && a.EventType == "OPENING_READINGS_SET")));
    }

    [Fact]
    public async Task Tenant_CanAlsoSetThem_ZeroIsValid()
    {
        var seed = await SeedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Put(seed.S.TenantClient, seed.InspectionId, new { electricity = 0, water = 0 })).StatusCode);
        Assert.Equal((0L, 0L), await ReadingsAsync(seed.S.TenancyId));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    [InlineData(null, 10)]
    [InlineData(10, null)]
    public async Task MissingOrNegative_Gets400(int? electricity, int? water)
    {
        var seed = await SeedAsync();
        await AssertError(await Put(seed.S.LandlordClient, seed.InspectionId, new { electricity, water }),
            HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal((null, null), await ReadingsAsync(seed.S.TenancyId));
    }

    [Fact]
    public async Task ChangingThem_AfterAConfirmation_CancelsIt_AndReopens()
    {
        var seed = await SeedAsync();
        await Put(seed.S.LandlordClient, seed.InspectionId, new { electricity = 100, water = 10 });
        await SetInspectionAsync(seed.InspectionId, InspectionStatus.AwaitingConfirm, factory.Clock.GetUtcNow());

        Assert.Equal(HttpStatusCode.OK, (await Put(seed.S.TenantClient, seed.InspectionId, new { electricity = 101, water = 10 })).StatusCode);

        var inspection = await factory.WithDbAsync(db => db.Inspections.AsNoTracking().SingleAsync(i => i.Id == seed.InspectionId));
        Assert.Equal(InspectionStatus.Open, inspection.Status);
        Assert.Null(inspection.LandlordConfirmedAt);
        Assert.Equal((101L, 10L), await ReadingsAsync(seed.S.TenancyId));
    }

    [Fact]
    public async Task SavingTheSameNumbersAgain_KeepsTheConfirmation()
    {
        var seed = await SeedAsync();
        await Put(seed.S.LandlordClient, seed.InspectionId, new { electricity = 100, water = 10 });
        await SetInspectionAsync(seed.InspectionId, InspectionStatus.AwaitingConfirm, factory.Clock.GetUtcNow());

        Assert.Equal(HttpStatusCode.OK, (await Put(seed.S.LandlordClient, seed.InspectionId, new { electricity = 100, water = 10 })).StatusCode);

        var inspection = await factory.WithDbAsync(db => db.Inspections.AsNoTracking().SingleAsync(i => i.Id == seed.InspectionId));
        Assert.Equal(InspectionStatus.AwaitingConfirm, inspection.Status);
        Assert.NotNull(inspection.LandlordConfirmedAt);
    }

    [Fact]
    public async Task LockedInspection_Gets409_RecordLocked_AndNothingChanges()
    {
        var seed = await SeedAsync();
        await Put(seed.S.LandlordClient, seed.InspectionId, new { electricity = 100, water = 10 });
        await SetInspectionAsync(seed.InspectionId, InspectionStatus.Locked);

        await AssertError(await Put(seed.S.LandlordClient, seed.InspectionId, new { electricity = 1, water = 1 }),
            HttpStatusCode.Conflict, "RECORD_LOCKED");
        Assert.Equal((100L, 10L), await ReadingsAsync(seed.S.TenancyId));
    }

    [Fact]
    public async Task MoveOutInspection_Gets409_InvalidState()
    {
        var seed = await SeedAsync();
        await factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == seed.InspectionId)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.Type, InspectionType.MoveOut)));

        await AssertError(await Put(seed.S.LandlordClient, seed.InspectionId, new { electricity = 1, water = 1 }),
            HttpStatusCode.Conflict, "INVALID_STATE");
        Assert.Equal((null, null), await ReadingsAsync(seed.S.TenancyId));
    }

    [Fact]
    public async Task Stranger_Gets404()
    {
        var seed = await SeedAsync();
        var stranger = factory.ClientFor(TestUser.Landlord($"x-{Guid.NewGuid():N}@example.com"));
        await AssertError(await Put(stranger, seed.InspectionId, new { electricity = 1, water = 1 }),
            HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
