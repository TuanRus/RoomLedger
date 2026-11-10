using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Gửi biên bản trả phòng để so sánh (MD-06-FE-01, BR-25 RULE 4, US-25 AC1).
 * POST /inspections/{id}/submit: chỉ người thuê, chỉ biên bản trả phòng OPEN; mỗi khu vực đủ ảnh READY
 * (không thì 409 INCOMPLETE_INSPECTION); OPEN → COMPARING đúng một lần; phát MoveOutSubmitted để Step Functions
 * so ảnh (MD-06-FE-02). Sau khi gửi: không thêm/xóa ảnh. Biên bản trả phòng không đi qua confirm/lock của MD-04.
 *
 * @author Sang
 * @version 1.0
 */
public class MoveOutSubmitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Seed(Scenario S, Guid MoveInId, Guid MoveOutId, JsonElement Areas);

    /** Biên bản nhận phòng LOCKED, biên bản trả phòng OPEN; không có ảnh. */
    private async Task<Seed> SeedAsync()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var moveIn = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        var moveInId = moveIn.GetProperty("inspectionId").GetGuid();
        await SetStatusAsync(moveInId, InspectionStatus.Locked);
        var moveOut = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-out", null)).DataAsync();
        return new Seed(s, moveInId, moveOut.GetProperty("inspectionId").GetGuid(), moveOut.GetProperty("areas").Clone());
    }

    private Task SetStatusAsync(Guid id, InspectionStatus status) =>
        factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == id).ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, status)));

    private async Task<Guid> UploadAsync(Seed seed, Guid areaId, FileStatus status)
    {
        var res = await seed.S.TenantClient.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "INSPECTION", targetId = seed.MoveOutId, areaId, fileName = "tra.jpg",
            contentType = "image/jpeg", size = 1000, sha256 = Scenario.Sha, captureSource = "CAMERA",
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.DataAsync()).GetProperty("fileId").GetGuid();
        await factory.WithDbAsync(db => db.Photos.Where(p => p.Id == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, status)));
        return id;
    }

    /** Đủ số ảnh READY tối thiểu cho mọi khu vực; trả id một ảnh để test xóa. */
    private async Task<Guid> FillAllAreasAsync(Seed seed)
    {
        var last = Guid.Empty;
        foreach (var area in seed.Areas.EnumerateArray())
        {
            for (var i = 0; i < area.GetProperty("minPhotos").GetInt32(); i++)
            {
                last = await UploadAsync(seed, area.GetProperty("areaId").GetGuid(), FileStatus.Ready);
            }
        }
        return last;
    }

    private static string SubmitUrl(Guid id) => $"/api/v1/inspections/{id}/submit";

    private Task<InspectionStatus> StatusAsync(Guid id) =>
        factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == id).Select(i => i.Status).SingleAsync());

    private int SubmittedEvents(Guid id) =>
        factory.Events.Published.Count(e => e.DetailType == "MoveOutSubmitted" && e.Detail.GetProperty("inspectionId").GetGuid() == id);

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AllAreasHaveEnoughPhotos_TenantSubmits_Comparing_AndTheCompareIsStarted()
    {
        var seed = await SeedAsync();
        await FillAllAreasAsync(seed);

        var res = await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("COMPARING", (await res.DataAsync()).GetProperty("status").GetString());
        Assert.Equal(InspectionStatus.Comparing, await StatusAsync(seed.MoveOutId));
        Assert.Equal(1, SubmittedEvents(seed.MoveOutId));
        var detail = factory.Events.Last("MoveOutSubmitted");
        Assert.Equal(seed.S.TenancyId, detail.GetProperty("tenancyId").GetGuid());
        Assert.Equal(seed.MoveInId, detail.GetProperty("moveInInspectionId").GetGuid());
        Assert.Equal(1, await factory.WithDbAsync(db =>
            db.AuditEvents.CountAsync(a => a.TargetId == seed.MoveOutId && a.EventType == "MOVE_OUT_SUBMITTED")));
    }

    [Fact]
    public async Task MissingPhotos_Gets409_IncompleteInspection_StaysOpen_NothingStarted()
    {
        var seed = await SeedAsync();
        await UploadAsync(seed, seed.Areas[0].GetProperty("areaId").GetGuid(), FileStatus.Ready);

        await AssertError(await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null), HttpStatusCode.Conflict, "INCOMPLETE_INSPECTION");

        Assert.Equal(InspectionStatus.Open, await StatusAsync(seed.MoveOutId));
        Assert.Equal(0, SubmittedEvents(seed.MoveOutId));
    }

    [Fact]
    public async Task PhotoStillProcessing_Gets409_IncompleteInspection()
    {
        var seed = await SeedAsync();
        await FillAllAreasAsync(seed);
        await UploadAsync(seed, seed.Areas[0].GetProperty("areaId").GetGuid(), FileStatus.Received);

        await AssertError(await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null), HttpStatusCode.Conflict, "INCOMPLETE_INSPECTION");
        Assert.Equal(InspectionStatus.Open, await StatusAsync(seed.MoveOutId));
    }

    [Fact]
    public async Task SubmittedTwice_Returns200_ButStartsTheCompareOnce()
    {
        var seed = await SeedAsync();
        await FillAllAreasAsync(seed);
        await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null);

        var again = await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, SubmittedEvents(seed.MoveOutId));
    }

    [Fact]
    public async Task TwoSubmitsAtOnce_StartTheCompareOnce()
    {
        var seed = await SeedAsync();
        await FillAllAreasAsync(seed);

        var results = await Task.WhenAll(
            seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null),
            seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, SubmittedEvents(seed.MoveOutId));
    }

    [Fact]
    public async Task AlreadyReviewing_Gets409_RecordLocked()
    {
        var seed = await SeedAsync();
        await SetStatusAsync(seed.MoveOutId, InspectionStatus.Reviewing);

        await AssertError(await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null), HttpStatusCode.Conflict, "RECORD_LOCKED");
        Assert.Equal(0, SubmittedEvents(seed.MoveOutId));
    }

    [Fact]
    public async Task Landlord_Gets403_Stranger_Gets404()
    {
        var seed = await SeedAsync();
        await FillAllAreasAsync(seed);
        var stranger = factory.ClientFor(TestUser.Tenant($"stranger-{Guid.NewGuid():N}@example.com"));

        await AssertError(await seed.S.LandlordClient.PostAsync(SubmitUrl(seed.MoveOutId), null), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertError(await stranger.PostAsync(SubmitUrl(seed.MoveOutId), null), HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Equal(InspectionStatus.Open, await StatusAsync(seed.MoveOutId));
    }

    [Fact]
    public async Task MoveInInspection_CannotBeSubmitted_Gets409_InvalidState()
    {
        var seed = await SeedAsync();
        await SetStatusAsync(seed.MoveInId, InspectionStatus.Open);

        await AssertError(await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveInId), null), HttpStatusCode.Conflict, "INVALID_STATE");
        Assert.Equal(InspectionStatus.Open, await StatusAsync(seed.MoveInId));
    }

    [Fact]
    public async Task AfterSubmit_PhotosCanNoLongerBeAddedOrDeleted()
    {
        var seed = await SeedAsync();
        var photoId = await FillAllAreasAsync(seed);
        await seed.S.TenantClient.PostAsync(SubmitUrl(seed.MoveOutId), null);

        var upload = await seed.S.TenantClient.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "INSPECTION", targetId = seed.MoveOutId, areaId = seed.Areas[0].GetProperty("areaId").GetGuid(),
            fileName = "muon.jpg", contentType = "image/jpeg", size = 1000, sha256 = Scenario.Sha,
        });
        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        await AssertError(await seed.S.TenantClient.DeleteAsync($"/api/v1/photos/{photoId}"), HttpStatusCode.Conflict, "RECORD_LOCKED");
    }

    [Fact]
    public async Task MoveOut_CannotBeConfirmedOrLocked_LikeAMoveIn()
    {
        var seed = await SeedAsync();
        await FillAllAreasAsync(seed);

        await AssertError(await seed.S.TenantClient.PostJsonAsync($"/api/v1/inspections/{seed.MoveOutId}/confirm", new { agreed = true }),
            HttpStatusCode.Conflict, "INVALID_STATE");
        await AssertError(await seed.S.LandlordClient.PostJsonAsync($"/api/v1/inspections/{seed.MoveOutId}/confirm", new { agreed = true }),
            HttpStatusCode.Conflict, "INVALID_STATE");
        Assert.Equal(InspectionStatus.Open, await StatusAsync(seed.MoveOutId));
    }
}
