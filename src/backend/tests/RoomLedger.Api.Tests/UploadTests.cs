using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Test tích hợp POST /uploads (presigned PUT kèm checksum).
 *
 * @author Tuấn
 * @version 1.0
 */
public class UploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task TenancyScan_ReturnsPresignedUrlAndPendingFile()
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var tenancy = await (await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null)).DataAsync();
        var versionId = tenancy.GetProperty("versions")[0].GetProperty("versionId").GetGuid();

        var res = await s.LandlordClient.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "TENANCY_VERSION", targetId = versionId, fileName = "hop-dong.pdf",
            contentType = "application/pdf", size = 3_000_000, sha256 = Scenario.Sha,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var data = await res.DataAsync();
        Assert.Equal("PUT", data.GetProperty("method").GetString());
        Assert.Equal(Scenario.Sha, data.GetProperty("headers").GetProperty("x-amz-checksum-sha256").GetString());
        var url = data.GetProperty("uploadUrl").GetString()!;
        Assert.EndsWith($"/{versionId}/{data.GetProperty("fileId").GetGuid()}.pdf", url);
        Assert.DoesNotContain("P.204", url);

        var detail = await (await s.LandlordClient.GetAsync($"/api/v1/tenancies/{tenancy.GetProperty("tenancyId").GetGuid()}")).DataAsync();
        var file = detail.GetProperty("versions")[0].GetProperty("files")[0];
        Assert.Equal("PENDING_UPLOAD", file.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("text/plain", 100)]
    [InlineData("application/pdf", 21L * 1024 * 1024)]
    public async Task TenancyScan_InvalidFile_ReturnsInvalidFile(string contentType, long size)
    {
        var s = await new Scenario(factory).WithRoomAsync();
        var tenancy = await (await s.LandlordClient.PostAsync($"/api/v1/rooms/{s.RoomId}/tenancies", null)).DataAsync();
        var res = await s.LandlordClient.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "TENANCY_VERSION", targetId = tenancy.GetProperty("versions")[0].GetProperty("versionId").GetGuid(),
            fileName = "x", contentType, size, sha256 = Scenario.Sha,
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("INVALID_FILE", (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task BadSha256_ReturnsValidationError()
    {
        var client = factory.ClientFor(TestUser.Landlord());
        var res = await client.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "TENANCY_VERSION", targetId = Guid.NewGuid(), fileName = "x.pdf", contentType = "application/pdf", size = 10,
            sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("sha256", (await res.JsonAsync()).GetProperty("details")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task InspectionPhoto_LockedInspection_ReturnsRecordLocked()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var areaId = await factory.WithDbAsync(db => db.RoomAreas.Where(a => a.RoomId == s.RoomId).Select(a => a.Id).FirstAsync());
        var inspection = new Inspection { TenancyId = s.TenancyId, Type = InspectionType.MoveIn, Status = InspectionStatus.Open };
        await factory.WithDbAsync(db => { db.Inspections.Add(inspection); return db.SaveChangesAsync(); });

        object Body() => new
        {
            targetType = "INSPECTION", targetId = inspection.Id, areaId, fileName = "IMG_2041.HEIC",
            contentType = "image/heic", size = 3482113, sha256 = Scenario.Sha,
        };
        var ok = await s.TenantClient.PostJsonAsync("/api/v1/uploads", Body());
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var photo = await factory.WithDbAsync(db => db.Photos.SingleAsync(p => p.InspectionId == inspection.Id));
        Assert.Equal(UserRole.Tenant, photo.UploaderRole);
        Assert.Equal(FileStatus.PendingUpload, photo.Status);

        await factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == inspection.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, InspectionStatus.Locked)));
        var locked = await s.TenantClient.PostJsonAsync("/api/v1/uploads", Body());
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        Assert.Equal("RECORD_LOCKED", (await locked.JsonAsync()).GetProperty("errorCode").GetString());

        var stranger = factory.ClientFor(TestUser.Tenant("x@example.com"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostJsonAsync("/api/v1/uploads", Body())).StatusCode);
    }

    [Fact]
    public async Task TenantCannotUploadContractScan()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var add = await (await s.LandlordClient.PostJsonAsync($"/api/v1/tenancies/{s.TenancyId}/versions", new { type = "ADDENDUM" })).DataAsync();
        var res = await s.TenantClient.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "TENANCY_VERSION", targetId = add.GetProperty("versionId").GetGuid(), fileName = "x.pdf",
            contentType = "application/pdf", size = 10, sha256 = Scenario.Sha,
        });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
