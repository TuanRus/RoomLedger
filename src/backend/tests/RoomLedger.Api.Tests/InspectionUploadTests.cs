using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * POST /uploads cho ảnh biên bản (MD-04-FE-02, BR-16), bổ sung cho UploadTests của Tuấn
 * (đã có: người thuê tải được, biên bản khóa → 409, người lạ → 404).
 * Ở đây: chủ nhà tải được, giới hạn 20 ảnh/khu vực, file sai, khu vực phòng khác, nguồn ảnh CAMERA/GALLERY,
 * nhật ký UPLOAD_REQUESTED.
 *
 * @author Sang
 * @version 1.0
 */
public class InspectionUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Seed(Scenario Scenario, Guid InspectionId, Guid AreaId);

    private async Task<Seed> SeedAsync(int existingPhotos = 0)
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var areaId = await factory.WithDbAsync(db => db.RoomAreas.Where(a => a.RoomId == s.RoomId).Select(a => a.Id).FirstAsync());
        var inspection = new Inspection { TenancyId = s.TenancyId, Type = InspectionType.MoveIn, Status = InspectionStatus.Open };
        await factory.WithDbAsync(db =>
        {
            db.Inspections.Add(inspection);
            for (var i = 0; i < existingPhotos; i++)
            {
                db.Photos.Add(new Photo
                {
                    InspectionId = inspection.Id, RoomAreaId = areaId, UploadedBy = s.Tenant.Id, UploaderRole = UserRole.Tenant,
                    FileName = $"old{i}.jpg", ContentType = "image/jpeg", Size = 1_000, Sha256 = Scenario.Sha, IncomingKey = $"seed/{i}.jpg",
                });
            }
            return db.SaveChangesAsync();
        });
        return new Seed(s, inspection.Id, areaId);
    }

    private static object Body(Seed s, Guid? areaId = null, string contentType = "image/heic", long size = 3_000_000,
        string? captureSource = "CAMERA") => new
    {
        targetType = "INSPECTION",
        targetId = s.InspectionId,
        areaId = areaId ?? s.AreaId,
        fileName = "IMG_0001.HEIC",
        contentType,
        size,
        sha256 = Scenario.Sha,
        captureSource,
    };

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Landlord_OfTheTenancy_CanUpload_AndIsRecordedAsLandlord()
    {
        var s = await SeedAsync();
        var res = await s.Scenario.LandlordClient.PostJsonAsync("/api/v1/uploads", Body(s));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var fileId = (await res.DataAsync()).GetProperty("fileId").GetGuid();
        var photo = await factory.WithDbAsync(db => db.Photos.SingleAsync(p => p.Id == fileId));
        Assert.Equal(UserRole.Landlord, photo.UploaderRole);
        Assert.EndsWith($"/{s.InspectionId}/{fileId}.heic", photo.IncomingKey);
    }

    [Fact]
    public async Task TwentyFirstPhotoInAnArea_Gets400_InvalidFile()
    {
        var s = await SeedAsync(existingPhotos: 20);
        await AssertError(await s.Scenario.TenantClient.PostJsonAsync("/api/v1/uploads", Body(s)), HttpStatusCode.BadRequest, "INVALID_FILE");
    }

    [Theory]
    [InlineData("image/gif", 1_000)]
    [InlineData("image/jpeg", 25L * 1024 * 1024 + 1)]
    public async Task WrongTypeOrTooBig_Gets400_InvalidFile(string contentType, long size)
    {
        var s = await SeedAsync();
        await AssertError(await s.Scenario.TenantClient.PostJsonAsync("/api/v1/uploads", Body(s, contentType: contentType, size: size)),
            HttpStatusCode.BadRequest, "INVALID_FILE");
    }

    [Fact]
    public async Task AreaOfAnotherRoom_Gets404()
    {
        var s = await SeedAsync();
        var other = await SeedAsync();
        await AssertError(await s.Scenario.TenantClient.PostJsonAsync("/api/v1/uploads", Body(s, areaId: other.AreaId)),
            HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData("CAMERA", CaptureSource.Camera)]
    [InlineData("GALLERY", CaptureSource.Gallery)]
    [InlineData(null, CaptureSource.Gallery)]     // FE cũ không gửi → coi như chọn từ thư viện
    public async Task CaptureSource_IsStoredOnThePhoto(string? sent, CaptureSource expected)
    {
        var s = await SeedAsync();
        var res = await s.Scenario.TenantClient.PostJsonAsync("/api/v1/uploads", Body(s, captureSource: sent));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var fileId = (await res.DataAsync()).GetProperty("fileId").GetGuid();
        Assert.Equal(expected, await factory.WithDbAsync(db => db.Photos.Where(p => p.Id == fileId).Select(p => p.CaptureSource).SingleAsync()));
    }

    [Fact]
    public async Task InspectionPhotoUpload_IsAudited()
    {
        var s = await SeedAsync();
        var res = await s.Scenario.TenantClient.PostJsonAsync("/api/v1/uploads", Body(s));
        var fileId = (await res.DataAsync()).GetProperty("fileId").GetGuid();
        var audit = await factory.WithDbAsync(db => db.AuditEvents.SingleOrDefaultAsync(a => a.TargetId == fileId && a.EventType == "UPLOAD_REQUESTED"));
        Assert.NotNull(audit);
        Assert.Equal(s.Scenario.Tenant.Id, audit!.ActorId);
    }
}
