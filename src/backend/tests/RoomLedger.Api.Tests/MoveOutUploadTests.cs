using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Tests;

/**
 * Chỉ người thuê gửi ảnh biên bản trả phòng (BR-25, quyết định D4 của Tuấn ngày 17/11). Chủ nhà xin link tải ảnh vào
 * biên bản trả phòng → 403 FORBIDDEN, không tạo dòng photo. Biên bản nhận phòng vẫn cho cả hai bên gửi ảnh (BR-16).
 *
 * @author Sang
 * @version 1.0
 */
public class MoveOutUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Seed(Scenario S, Guid MoveInId, Guid MoveOutId, Guid AreaId);

    private async Task<Seed> SeedAsync()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var moveIn = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        var moveInId = moveIn.GetProperty("inspectionId").GetGuid();
        var areaId = moveIn.GetProperty("areas")[0].GetProperty("areaId").GetGuid();
        await factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == moveInId)
            .ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, InspectionStatus.Locked)));
        var moveOut = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-out", null)).DataAsync();
        return new Seed(s, moveInId, moveOut.GetProperty("inspectionId").GetGuid(), areaId);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid inspectionId, Guid areaId) =>
        client.PostJsonAsync("/api/v1/uploads", new
        {
            targetType = "INSPECTION", targetId = inspectionId, areaId, fileName = "anh.jpg",
            contentType = "image/jpeg", size = 1000, sha256 = Scenario.Sha, captureSource = "CAMERA",
        });

    private Task<int> PhotoCountAsync(Guid inspectionId) =>
        factory.WithDbAsync(db => db.Photos.CountAsync(p => p.InspectionId == inspectionId));

    [Fact]
    public async Task Landlord_CannotUploadToTheMoveOut_Gets403_NoPhotoCreated()
    {
        var seed = await SeedAsync();

        var res = await UploadAsync(seed.S.LandlordClient, seed.MoveOutId, seed.AreaId);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("FORBIDDEN", (await res.JsonAsync()).GetProperty("errorCode").GetString());
        Assert.Equal(0, await PhotoCountAsync(seed.MoveOutId));
    }

    [Fact]
    public async Task Tenant_UploadsToTheMoveOut()
    {
        var seed = await SeedAsync();

        var res = await UploadAsync(seed.S.TenantClient, seed.MoveOutId, seed.AreaId);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(1, await PhotoCountAsync(seed.MoveOutId));
    }

    [Fact]
    public async Task Landlord_StillUploadsToAnOpenMoveIn()
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var moveIn = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();

        var res = await UploadAsync(s.LandlordClient, moveIn.GetProperty("inspectionId").GetGuid(),
            moveIn.GetProperty("areas")[0].GetProperty("areaId").GetGuid());

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }
}
