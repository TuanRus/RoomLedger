using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Kỳ trả phòng và ảnh đồng hồ tháng cuối (MD-06-FE-01, BR-25 RULE 3; quyết định A3, B1, B2 của Tuấn ngày 17/11).
 * Mở biên bản trả phòng chốt finalPeriod (tháng giờ Việt Nam lúc mở). Gửi biên bản cần chỉ số điện và nước của kỳ đó,
 * ảnh đã tới hoặc đã có số; thiếu → 409 FINAL_READING_MISSING, biên bản vẫn OPEN, không khởi động so sánh.
 * Không cần chủ nhà xác nhận chỉ số (việc đó kiểm lúc quyết toán).
 *
 * @author Sang
 * @version 1.0
 */
public class MoveOutFinalReadingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Period = "2026-11";   // ApiFactory.Clock: 2026-11-20 03:00 UTC = 10:00 giờ Việt Nam

    private sealed record Seed(Scenario S, Guid MoveInId, Guid MoveOutId, JsonElement MoveOut);

    /** Biên bản nhận phòng LOCKED; biên bản trả phòng OPEN, mọi khu vực đủ ảnh READY; chưa có chỉ số. */
    private async Task<Seed> SeedAsync()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var moveIn = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        var moveInId = moveIn.GetProperty("inspectionId").GetGuid();
        await factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == moveInId)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, InspectionStatus.Locked)));
        var moveOut = (await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-out", null)).DataAsync()).Clone();
        var moveOutId = moveOut.GetProperty("inspectionId").GetGuid();
        var tenantId = await factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == s.TenancyId).Select(t => t.TenantId!.Value).SingleAsync());
        foreach (var area in moveOut.GetProperty("areas").EnumerateArray())
        {
            for (var i = 0; i < area.GetProperty("minPhotos").GetInt32(); i++)
            {
                await AddPhotoAsync(moveOutId, area.GetProperty("areaId").GetGuid(), tenantId);
            }
        }
        return new Seed(s, moveInId, moveOutId, moveOut);
    }

    private Task AddPhotoAsync(Guid inspectionId, Guid areaId, Guid tenantId) => factory.WithDbAsync(async db =>
    {
        db.Photos.Add(new Photo
        {
            InspectionId = inspectionId, RoomAreaId = areaId, UploadedBy = tenantId, UploaderRole = UserRole.Tenant,
            Status = FileStatus.Ready, FileName = "tra.jpg", ContentType = "image/jpeg", Size = 1000, Sha256 = Scenario.Sha,
            IncomingKey = $"inspections/{inspectionId}/{Guid.NewGuid():N}.jpg",
        });
        return await db.SaveChangesAsync();
    });

    private Task ReadingAsync(Seed seed, MeterKind kind, FileStatus? photo, MeterReadingStatus status = MeterReadingStatus.PendingAi,
        string period = Period) =>
        factory.WithDbAsync(async db =>
        {
            db.MeterReadings.Add(new MeterReading
            {
                RoomId = seed.S.RoomId, TenancyId = seed.S.TenancyId, Period = period, Kind = kind, Status = status,
                PhotoStatus = photo, PhotoKey = photo is null ? null : $"meters/{Guid.NewGuid():N}.jpg",
                Value = status is MeterReadingStatus.PendingAi ? null : 1234,
            });
            return await db.SaveChangesAsync();
        });

    private static string SubmitUrl(Guid id) => $"/api/v1/inspections/{id}/submit";

    private Task<InspectionStatus> StatusAsync(Guid id) =>
        factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == id).Select(i => i.Status).SingleAsync());

    private int SubmittedEvents(Guid id) =>
        factory.Events.Published.Count(e => e.DetailType == "MoveOutSubmitted" && e.Detail.GetProperty("inspectionId").GetGuid() == id);

    private async Task AssertMissing(Seed seed, string expectedMessage)
    {
        var res = await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.Equal("FINAL_READING_MISSING", body.GetProperty("errorCode").GetString());
        Assert.Equal(expectedMessage, body.GetProperty("message").GetString());
        Assert.Equal(InspectionStatus.Open, await StatusAsync(seed.MoveOutId));
        Assert.Equal(0, SubmittedEvents(seed.MoveOutId));
    }

    [Fact]
    public async Task OpeningAMoveOut_FixesTheFinalPeriod_ShownOnTheInspection()
    {
        var seed = await SeedAsync();

        Assert.Equal(Period, seed.MoveOut.GetProperty("finalPeriod").GetString());
        var get = await (await seed.S.LandlordClient.GetAsync($"/api/v1/inspections/{seed.MoveOutId}")).DataAsync();
        Assert.Equal(Period, get.GetProperty("finalPeriod").GetString());
    }

    [Fact]
    public async Task MoveIn_HasNoFinalPeriod()
    {
        var seed = await SeedAsync();

        var get = await (await seed.S.LandlordClient.GetAsync($"/api/v1/inspections/{seed.MoveInId}")).DataAsync();

        Assert.True(!get.TryGetProperty("finalPeriod", out var p) || p.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task NoFinalReadings_Gets409_FinalReadingMissing_NamingBothMeters()
    {
        var seed = await SeedAsync();

        await AssertMissing(seed, "Thiếu ảnh đồng hồ: điện, nước (kỳ 2026-11)");
    }

    [Fact]
    public async Task WaterPhotoNeverArrived_Gets409_NamingWaterOnly()
    {
        var seed = await SeedAsync();
        await ReadingAsync(seed, MeterKind.Electricity, FileStatus.Received);
        await ReadingAsync(seed, MeterKind.Water, FileStatus.PendingUpload);

        await AssertMissing(seed, "Thiếu ảnh đồng hồ: nước (kỳ 2026-11)");
    }

    [Fact]
    public async Task ReadingsOfAnotherPeriod_DoNotCount()
    {
        var seed = await SeedAsync();
        await ReadingAsync(seed, MeterKind.Electricity, FileStatus.Ready, period: "2026-10");
        await ReadingAsync(seed, MeterKind.Water, FileStatus.Ready, period: "2026-10");

        await AssertMissing(seed, "Thiếu ảnh đồng hồ: điện, nước (kỳ 2026-11)");
    }

    [Fact]
    public async Task BothPhotosArrived_NotYetReadOrConfirmed_Submits()
    {
        var seed = await SeedAsync();
        await ReadingAsync(seed, MeterKind.Electricity, FileStatus.Received);
        await ReadingAsync(seed, MeterKind.Water, FileStatus.Ready);

        var res = await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(InspectionStatus.Reviewing, await StatusAsync(seed.MoveOutId));
        Assert.Equal(1, SubmittedEvents(seed.MoveOutId));
    }

    [Fact]
    public async Task ReadingThatAlreadyHasAValue_Counts_EvenIfThePhotoStatusWasNeverUpdated()
    {
        var seed = await SeedAsync();
        await ReadingAsync(seed, MeterKind.Electricity, FileStatus.PendingUpload, MeterReadingStatus.PendingConfirm);
        await ReadingAsync(seed, MeterKind.Water, FileStatus.PendingUpload, MeterReadingStatus.Confirmed);

        var res = await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
