using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;

namespace RoomLedger.Api.Tests;

/**
 * Danh sách ảnh của biên bản (MD-04 màn hình biên bản, MD-06-FE-01 ảnh nhận phòng để đối chiếu).
 * GET /inspections/{id}/photos: chỉ hai bên của hợp đồng; URL xem và thumbnail là presigned GET tới bucket derived;
 * không lộ khóa S3; ảnh chưa tải lên (PENDING_UPLOAD) không hiện; canDelete chỉ đúng với người gửi trước khi khóa.
 *
 * @author Sang
 * @version 1.0
 */
public class InspectionPhotosTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Seed(Scenario S, Guid InspectionId, Guid AreaId);

    private async Task<Seed> SeedAsync()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var inspection = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        return new Seed(s, inspection.GetProperty("inspectionId").GetGuid(),
            inspection.GetProperty("areas")[0].GetProperty("areaId").GetGuid());
    }

    /** Người thuê xin URL tải ảnh rồi đặt trạng thái ảnh như sau khi pipeline chạy. */
    private async Task<(Guid Id, string Key)> AddPhotoAsync(Seed seed, FileStatus status, string fileName = "mo.jpg")
    {
        var upload = await seed.S.TenantClient.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "INSPECTION", targetId = seed.InspectionId, areaId = seed.AreaId, fileName,
            contentType = "image/jpeg", size = 1000, sha256 = Scenario.Sha, captureSource = "CAMERA",
        });
        var id = (await upload.DataAsync()).GetProperty("fileId").GetGuid();
        var key = await factory.WithDbAsync(db => db.Photos.Where(p => p.Id == id).Select(p => p.IncomingKey).SingleAsync());
        var derived = status is FileStatus.Ready or FileStatus.Sealed ? PhotoRules.ViewKey(key) : null;
        await factory.WithDbAsync(db => db.Photos.Where(p => p.Id == id).ExecuteUpdateAsync(x => x
            .SetProperty(p => p.Status, status).SetProperty(p => p.DerivedKey, derived)));
        return (id, key);
    }

    private static async Task<JsonElement[]> PhotosAsync(HttpClient client, Guid inspectionId)
    {
        var res = await client.GetAsync($"/api/v1/inspections/{inspectionId}/photos");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.DataAsync()).EnumerateArray().ToArray();
    }

    [Fact]
    public async Task ReadyPhoto_HasViewAndThumbnailUrlsFromDerived_AndNoS3Keys()
    {
        var seed = await SeedAsync();
        var (id, key) = await AddPhotoAsync(seed, FileStatus.Ready);

        var photo = Assert.Single(await PhotosAsync(seed.S.TenantClient, seed.InspectionId));

        Assert.Equal(id, photo.GetProperty("photoId").GetGuid());
        Assert.Equal(seed.AreaId, photo.GetProperty("areaId").GetGuid());
        Assert.Equal("mo.jpg", photo.GetProperty("fileName").GetString());
        Assert.Equal("READY", photo.GetProperty("status").GetString());
        Assert.Equal("TENANT", photo.GetProperty("uploaderRole").GetString());
        Assert.Equal("CAMERA", photo.GetProperty("captureSource").GetString());
        Assert.False(photo.GetProperty("oldPhotoWarning").GetBoolean());
        Assert.Equal($"https://derived.example/{PhotoRules.ViewKey(key)}", photo.GetProperty("viewUrl").GetString());
        Assert.Equal($"https://derived.example/{PhotoRules.ThumbnailKey(key)}", photo.GetProperty("thumbnailUrl").GetString());
        var raw = photo.GetRawText();
        Assert.DoesNotContain("incomingKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sha256", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploaderCanDelete_OtherPartyCannot_BeforeLock()
    {
        var seed = await SeedAsync();
        await AddPhotoAsync(seed, FileStatus.Ready);

        var asTenant = Assert.Single(await PhotosAsync(seed.S.TenantClient, seed.InspectionId));
        var asLandlord = Assert.Single(await PhotosAsync(seed.S.LandlordClient, seed.InspectionId));

        Assert.True(asTenant.GetProperty("uploadedByMe").GetBoolean());
        Assert.True(asTenant.GetProperty("canDelete").GetBoolean());
        Assert.False(asLandlord.GetProperty("uploadedByMe").GetBoolean());
        Assert.False(asLandlord.GetProperty("canDelete").GetBoolean());
    }

    [Fact]
    public async Task AfterLock_NobodyCanDelete()
    {
        var seed = await SeedAsync();
        await AddPhotoAsync(seed, FileStatus.Sealed);
        await factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == seed.InspectionId)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, InspectionStatus.Locked)));

        var photo = Assert.Single(await PhotosAsync(seed.S.TenantClient, seed.InspectionId));

        Assert.False(photo.GetProperty("canDelete").GetBoolean());
        Assert.Equal("SEALED", photo.GetProperty("status").GetString());
        Assert.StartsWith("https://derived.example/", photo.GetProperty("viewUrl").GetString());
    }

    [Fact]
    public async Task NotYetUploaded_IsHidden_StillProcessing_HasNoUrls()
    {
        var seed = await SeedAsync();
        await AddPhotoAsync(seed, FileStatus.PendingUpload, "chua-tai.jpg");
        await AddPhotoAsync(seed, FileStatus.Received, "dang-xu-ly.jpg");

        var photo = Assert.Single(await PhotosAsync(seed.S.TenantClient, seed.InspectionId));

        Assert.Equal("dang-xu-ly.jpg", photo.GetProperty("fileName").GetString());
        Assert.Equal(JsonValueKind.Null, photo.GetProperty("viewUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, photo.GetProperty("thumbnailUrl").ValueKind);
    }

    [Fact]
    public async Task Photos_AreInUploadOrder()
    {
        var seed = await SeedAsync();
        var first = await AddPhotoAsync(seed, FileStatus.Ready, "1.jpg");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await AddPhotoAsync(seed, FileStatus.Ready, "2.jpg");

        var ids = (await PhotosAsync(seed.S.TenantClient, seed.InspectionId)).Select(p => p.GetProperty("photoId").GetGuid());

        Assert.Equal([first.Id, second.Id], ids);
    }

    [Fact]
    public async Task Stranger_AndUnknownInspection_Get404()
    {
        var seed = await SeedAsync();
        await AddPhotoAsync(seed, FileStatus.Ready);
        var stranger = factory.ClientFor(TestUser.Tenant("stranger-photos@example.com"));

        var res = await stranger.GetAsync($"/api/v1/inspections/{seed.InspectionId}/photos");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("NOT_FOUND", (await res.JsonAsync()).GetProperty("errorCode").GetString());

        var unknown = await seed.S.TenantClient.GetAsync($"/api/v1/inspections/{Guid.NewGuid()}/photos");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
