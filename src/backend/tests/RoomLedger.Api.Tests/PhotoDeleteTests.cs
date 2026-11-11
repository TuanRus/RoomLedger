using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;

namespace RoomLedger.Api.Tests;

/**
 * Xóa ảnh trước khi khóa (MD-04-FE-03, BR-17, US-17, kiểm thử bảo mật K1).
 * DELETE /photos/{photoId}: chỉ người gửi, chỉ khi biên bản OPEN hoặc AWAITING_CONFIRM; xóa object S3 (incoming,
 * bản xem, thumbnail); đang chờ xác nhận thì hủy xác nhận và mở lại biên bản; ghi PHOTO_DELETED / PHOTO_DELETE_DENIED.
 *
 * @author Sang
 * @version 1.0
 */
public class PhotoDeleteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Seed(Scenario S, Guid InspectionId, Guid PhotoId, string IncomingKey);

    /** Hợp đồng ACTIVE, biên bản nhận phòng mở, người thuê gửi 1 ảnh; S3 giả có ảnh gốc, bản xem và thumbnail. */
    private async Task<Seed> SeedAsync()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var inspection = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        var inspectionId = inspection.GetProperty("inspectionId").GetGuid();
        var areaId = inspection.GetProperty("areas")[0].GetProperty("areaId").GetGuid();
        var upload = await s.TenantClient.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "INSPECTION", targetId = inspectionId, areaId, fileName = "mo.jpg",
            contentType = "image/jpeg", size = 1000, sha256 = Scenario.Sha, captureSource = "CAMERA",
        });
        var photoId = (await upload.DataAsync()).GetProperty("fileId").GetGuid();
        var key = await factory.WithDbAsync(db => db.Photos.Where(p => p.Id == photoId).Select(p => p.IncomingKey).SingleAsync());
        factory.ObjectStore.Objects[(StorageBucket.Incoming, key)] = [1];
        factory.ObjectStore.Objects[(StorageBucket.Derived, PhotoRules.ViewKey(key))] = [2];
        factory.ObjectStore.Objects[(StorageBucket.Derived, PhotoRules.ThumbnailKey(key))] = [3];
        return new Seed(s, inspectionId, photoId, key);
    }

    private Task<int> AuditCount(Guid photoId, string type) =>
        factory.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.TargetId == photoId && a.EventType == type));

    private Task<bool> PhotoExists(Guid photoId) => factory.WithDbAsync(db => db.Photos.AnyAsync(p => p.Id == photoId));

    private Task SetInspectionAsync(Guid id, InspectionStatus status, DateTimeOffset? tenantConfirmedAt = null) =>
        factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == id).ExecuteUpdateAsync(x => x
            .SetProperty(i => i.Status, status).SetProperty(i => i.TenantConfirmedAt, tenantConfirmedAt)));

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Uploader_DeletesOwnPhoto_BeforeLock_RemovesRowAndS3Objects()
    {
        var seed = await SeedAsync();

        var res = await seed.S.TenantClient.DeleteAsync($"/api/v1/photos/{seed.PhotoId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(await PhotoExists(seed.PhotoId));
        Assert.DoesNotContain(factory.ObjectStore.Objects.Keys, k => k.Key.StartsWith(seed.IncomingKey[..^4]));
        Assert.Equal(1, await AuditCount(seed.PhotoId, "PHOTO_DELETED"));
        var bedroom = (await res.DataAsync()).GetProperty("areas")[0];      // trả lại biên bản để FE vẽ lại
        Assert.Equal(0, bedroom.GetProperty("photoCount").GetInt32());
    }

    [Fact]
    public async Task OtherPartyOfTheTenancy_Gets403_AndTheRefusalIsAudited()
    {
        var seed = await SeedAsync();

        await AssertError(await seed.S.LandlordClient.DeleteAsync($"/api/v1/photos/{seed.PhotoId}"), HttpStatusCode.Forbidden, "FORBIDDEN");

        Assert.True(await PhotoExists(seed.PhotoId));
        Assert.Equal(1, await AuditCount(seed.PhotoId, "PHOTO_DELETE_DENIED"));
        Assert.True(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Incoming, seed.IncomingKey)));
    }

    [Fact]
    public async Task K1_LockedInspection_Gets409_RecordLocked_AndTheRefusalIsAudited()
    {
        var seed = await SeedAsync();
        await SetInspectionAsync(seed.InspectionId, InspectionStatus.Locked);

        var res = await seed.S.TenantClient.DeleteAsync($"/api/v1/photos/{seed.PhotoId}");

        await AssertError(res, HttpStatusCode.Conflict, "RECORD_LOCKED");
        Assert.True(await PhotoExists(seed.PhotoId));
        Assert.Equal(1, await AuditCount(seed.PhotoId, "PHOTO_DELETE_DENIED"));
        Assert.True(factory.ObjectStore.Objects.ContainsKey((StorageBucket.Incoming, seed.IncomingKey)));
    }

    [Fact]
    public async Task Stranger_Gets404()
    {
        var seed = await SeedAsync();
        var stranger = factory.ClientFor(TestUser.Tenant("stranger@example.com"));
        await AssertError(await stranger.DeleteAsync($"/api/v1/photos/{seed.PhotoId}"), HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.True(await PhotoExists(seed.PhotoId));
    }

    [Fact]
    public async Task UnknownPhoto_Gets404()
    {
        var seed = await SeedAsync();
        await AssertError(await seed.S.TenantClient.DeleteAsync($"/api/v1/photos/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task DeletingWhileAwaitingConfirm_CancelsConfirmation_AndReopens()
    {
        var seed = await SeedAsync();
        await SetInspectionAsync(seed.InspectionId, InspectionStatus.AwaitingConfirm, factory.Clock.GetUtcNow());

        var res = await seed.S.TenantClient.DeleteAsync($"/api/v1/photos/{seed.PhotoId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var inspection = await factory.WithDbAsync(db => db.Inspections.AsNoTracking().SingleAsync(i => i.Id == seed.InspectionId));
        Assert.Equal(InspectionStatus.Open, inspection.Status);
        Assert.Null(inspection.TenantConfirmedAt);
        Assert.Null(inspection.LandlordConfirmedAt);
    }

    [Fact]
    public async Task DeletingAfterTheLandlordConfirmed_TellsTheLandlord_AndAuditsReopened()   // review #15 điểm 2 (Tuấn)
    {
        var seed = await SeedAsync();
        await factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == seed.InspectionId).ExecuteUpdateAsync(x => x
            .SetProperty(i => i.Status, InspectionStatus.AwaitingConfirm)
            .SetProperty(i => i.LandlordConfirmedAt, factory.Clock.GetUtcNow())));
        var landlordId = await factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == seed.S.TenancyId).Select(t => t.LandlordId).SingleAsync());

        Assert.Equal(HttpStatusCode.OK, (await seed.S.TenantClient.DeleteAsync($"/api/v1/photos/{seed.PhotoId}")).StatusCode);

        var evt = factory.Events.Last("InspectionReopened");
        Assert.Equal(seed.InspectionId, evt.GetProperty("inspectionId").GetGuid());
        Assert.Equal(landlordId, evt.GetProperty("notifyUserId").GetGuid());
        Assert.Equal("PHOTO_DELETED", evt.GetProperty("reason").GetString());
        var detail = await factory.WithDbAsync(db => db.AuditEvents
            .Where(a => a.TargetId == seed.PhotoId && a.EventType == "PHOTO_DELETED").Select(a => a.DetailJson).SingleAsync());
        Assert.Contains("\"reopened\":true", detail);
    }

    [Fact]
    public async Task DeletingWithNoConfirmation_NoReopenedEvent()
    {
        var seed = await SeedAsync();
        await seed.S.TenantClient.DeleteAsync($"/api/v1/photos/{seed.PhotoId}");
        Assert.DoesNotContain(factory.Events.Published, e => e.DetailType == "InspectionReopened"
            && e.Detail.GetProperty("inspectionId").GetGuid() == seed.InspectionId);
    }
}
