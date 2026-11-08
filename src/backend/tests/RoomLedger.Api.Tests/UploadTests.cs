using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * POST /api/v1/uploads cho ảnh biên bản (BR-16 RULE 1–2, EXCEPTION 1 và 3): kiểm quyền, trạng thái, file,
 * tạo bản ghi photo PENDING_UPLOAD và trả presigned PUT bắt buộc x-amz-checksum-sha256.
 *
 * @author Sang
 * @version 1.0
 */
public class UploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string Sha = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private sealed record Seed(Guid LandlordId, TestUser Landlord, TestUser Tenant, Guid TenancyId, Guid InspectionId, Guid AreaId);

    private static object Body(Seed s, Guid? areaId = null, string contentType = "image/heic", long size = 3_000_000,
        string captureSource = "CAMERA") => new
    {
        targetType = "INSPECTION",
        targetId = s.InspectionId,
        roomAreaId = areaId ?? s.AreaId,
        fileName = "IMG_0001.HEIC",
        contentType,
        size,
        sha256 = Sha,
        captureSource,
    };

    [Fact]
    public async Task Tenant_GetsPresignedPutWithChecksum_AndPendingPhotoIsRecorded()
    {
        var s = await SeedAsync();

        var res = await factory.ClientFor(s.Tenant).PostJsonAsync("/api/v1/uploads", Body(s));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var data = await res.DataAsync();
        var photoId = data.GetProperty("photoId").GetGuid();
        Assert.Equal("PUT", data.GetProperty("method").GetString());
        Assert.EndsWith($"/{s.LandlordId}/{s.TenancyId}/{s.InspectionId}/{photoId}.heic", data.GetProperty("uploadUrl").GetString());
        Assert.Equal(Sha, data.GetProperty("headers").GetProperty("x-amz-checksum-sha256").GetString());

        var photo = await factory.WithDbAsync(db => db.Photos.SingleAsync(p => p.Id == photoId));
        Assert.Equal(FileStatus.PendingUpload, photo.Status);
        Assert.Equal(s.Tenant.Id, photo.UploadedBy);
        Assert.Equal(UserRole.Tenant, photo.UploaderRole);
        Assert.Equal(s.AreaId, photo.RoomAreaId);
        Assert.Equal(Sha, photo.Sha256);
        Assert.Equal($"{s.LandlordId}/{s.TenancyId}/{s.InspectionId}/{photoId}.heic", photo.IncomingKey);

        var source = await factory.WithDbAsync(db => db.Database
            .SqlQuery<string>($"SELECT capture_source AS \"Value\" FROM photo WHERE id = {photoId}").SingleAsync());
        Assert.Equal("CAMERA", source);

        var audits = await factory.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.TargetId == photoId && a.EventType == "UPLOAD_REQUESTED"));
        Assert.Equal(1, audits);
    }

    [Fact]
    public async Task Landlord_OfTheTenancy_CanAlsoUpload()
    {
        var s = await SeedAsync();
        var res = await factory.ClientFor(s.Landlord).PostJsonAsync("/api/v1/uploads", Body(s, captureSource: "GALLERY"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Stranger_Gets404_NotFound()
    {
        var s = await SeedAsync();
        var res = await factory.ClientFor(TestUser.Tenant("stranger@example.com")).PostJsonAsync("/api/v1/uploads", Body(s));
        await AssertError(res, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task LockedInspection_Gets409_RecordLocked()
    {
        var s = await SeedAsync(InspectionStatus.Locked);
        var res = await factory.ClientFor(s.Tenant).PostJsonAsync("/api/v1/uploads", Body(s));
        await AssertError(res, HttpStatusCode.Conflict, "RECORD_LOCKED");
    }

    [Theory]
    [InlineData("image/gif", 1_000)]
    [InlineData("image/jpeg", 25L * 1024 * 1024 + 1)]
    public async Task WrongTypeOrTooBig_Gets400_InvalidFile(string contentType, long size)
    {
        var s = await SeedAsync();
        var res = await factory.ClientFor(s.Tenant).PostJsonAsync("/api/v1/uploads", Body(s, contentType: contentType, size: size));
        await AssertError(res, HttpStatusCode.BadRequest, "INVALID_FILE");
    }

    [Fact]
    public async Task TwentyFirstPhotoInAnArea_Gets400_InvalidFile()
    {
        var s = await SeedAsync(existingPhotos: 20);
        var res = await factory.ClientFor(s.Tenant).PostJsonAsync("/api/v1/uploads", Body(s));
        await AssertError(res, HttpStatusCode.BadRequest, "INVALID_FILE");
    }

    [Fact]
    public async Task AreaOfAnotherRoom_Gets400_ValidationError()
    {
        var s = await SeedAsync();
        var other = await SeedAsync();
        var res = await factory.ClientFor(s.Tenant).PostJsonAsync("/api/v1/uploads", Body(s, areaId: other.AreaId));
        await AssertError(res, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task WithoutToken_Gets401()
    {
        var s = await SeedAsync();
        var res = await factory.CreateClient().PostJsonAsync("/api/v1/uploads", Body(s));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    private async Task<Seed> SeedAsync(InspectionStatus status = InspectionStatus.Open, int existingPhotos = 0)
    {
        var landlord = TestUser.Landlord($"landlord-{Guid.NewGuid():N}@example.com");
        var tenant = TestUser.Tenant($"tenant-{Guid.NewGuid():N}@example.com");
        return await factory.WithDbAsync(async db =>
        {
            var property = new Property { LandlordId = landlord.Id, Name = "Nhà trọ Hoa Sen", Address = "12 Lê Lợi, Q1" };
            var room = new Room { Property = property, LandlordId = landlord.Id, Code = $"P.{Random.Shared.Next(100, 999)}", MonthlyRent = 3_500_000 };
            var area = new RoomArea { RoomId = room.Id, Name = "Phòng ngủ", MinPhotos = 2, SortOrder = 1 };
            room.Areas.Add(area);
            var tenancy = new Tenancy { Room = room, LandlordId = landlord.Id, TenantId = tenant.Id, Status = TenancyStatus.Active };
            var inspection = new Inspection { Tenancy = tenancy, Type = InspectionType.MoveIn, Status = status };
            db.Inspections.Add(inspection);
            for (var i = 0; i < existingPhotos; i++)
            {
                db.Photos.Add(new Photo
                {
                    InspectionId = inspection.Id, RoomAreaId = area.Id, UploadedBy = tenant.Id, UploaderRole = UserRole.Tenant,
                    FileName = $"old{i}.jpg", ContentType = "image/jpeg", Size = 1_000, Sha256 = Sha, IncomingKey = $"seed/{i}.jpg",
                });
            }
            await db.SaveChangesAsync();
            return new Seed(landlord.Id, landlord, tenant, tenancy.Id, inspection.Id, area.Id);
        });
    }
}
