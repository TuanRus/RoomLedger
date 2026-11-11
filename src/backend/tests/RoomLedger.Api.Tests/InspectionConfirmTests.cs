using System.Net;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Api.Tests.Infrastructure;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;

namespace RoomLedger.Api.Tests;

/**
 * Xác nhận và khóa biên bản nhận phòng (MD-04-FE-04, BR-18 RULE 1–3).
 * POST /inspections/{id}/confirm { agreed: true }: đủ ảnh READY mỗi khu vực, ghi xác nhận của bên gọi,
 * đủ hai bên → LOCKED đúng một lần (UPDATE có điều kiện), phát InspectionConfirmed / InspectionLocked.
 *
 * @author Sang
 * @version 1.0
 */
public class InspectionConfirmTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Seed(Scenario S, Guid InspectionId);

    private static string ConfirmUrl(Guid id) => $"/api/v1/inspections/{id}/confirm";
    private static readonly object Agreed = new { agreed = true };

    /**
     * Hợp đồng ACTIVE + biên bản nhận phòng mở; complete = mỗi khu vực đủ ảnh READY;
     * readings = đã nhập chỉ số điện nước ban đầu (BR-18 RULE 5, Sang).
     */
    private async Task<Seed> SeedAsync(bool complete = true, FileStatus status = FileStatus.Ready, bool readings = true)
    {
        var s = await new Scenario(factory).WithActiveTenancyAsync();
        var opened = await (await s.TenantClient.PostAsync($"/api/v1/tenancies/{s.TenancyId}/inspections/move-in", null)).DataAsync();
        var inspectionId = opened.GetProperty("inspectionId").GetGuid();
        var areas = await factory.WithDbAsync(db => db.Inspections.Where(i => i.Id == inspectionId).Select(i => i.AreaSnapshotJson).SingleAsync());
        await factory.WithDbAsync(db =>
        {
            foreach (var area in InspectionService.ParseSnapshot(areas))
            {
                var count = complete ? area.MinPhotos : area.MinPhotos - 1;
                for (var i = 0; i < count; i++)
                {
                    var photo = new Photo
                    {
                        InspectionId = inspectionId, RoomAreaId = area.RoomAreaId, UploadedBy = s.Tenant.Id, UploaderRole = UserRole.Tenant,
                        FileName = $"{i}.jpg", ContentType = "image/jpeg", Size = 1_000, Sha256 = Scenario.Sha, Status = status,
                    };
                    photo.IncomingKey = $"seed/{inspectionId}/{photo.Id}.jpg";
                    db.Photos.Add(photo);
                }
            }
            return db.SaveChangesAsync();
        });
        if (readings)
        {
            await factory.WithDbAsync(db => db.Tenancies.Where(t => t.Id == s.TenancyId).ExecuteUpdateAsync(x => x
                .SetProperty(t => t.OpeningElectricity, 1200L).SetProperty(t => t.OpeningWater, 35L)));
        }
        return new Seed(s, inspectionId);
    }

    [Fact]
    public async Task MissingOpeningReadings_Gets409_IncompleteInspection_NamingThem()   // BR-18 RULE 5 (Sang)
    {
        var seed = await SeedAsync(readings: false);

        var res = await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.JsonAsync();
        Assert.Equal("INCOMPLETE_INSPECTION", body.GetProperty("errorCode").GetString());
        Assert.Contains("chỉ số", body.GetProperty("message").GetString());
        var inspection = await LoadAsync(seed.InspectionId);
        Assert.Equal(InspectionStatus.Open, inspection.Status);
        Assert.Null(inspection.TenantConfirmedAt);
    }

    private Task<Inspection> LoadAsync(Guid id) => factory.WithDbAsync(db => db.Inspections.AsNoTracking().SingleAsync(i => i.Id == id));

    private int EventCount(string type, Guid inspectionId) =>
        factory.Events.Published.Count(e => e.DetailType == type && e.Detail.GetProperty("inspectionId").GetGuid() == inspectionId);

    private Task<int> AuditCount(Guid id, string type) =>
        factory.WithDbAsync(db => db.AuditEvents.CountAsync(a => a.TargetId == id && a.EventType == type));

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.Equal(errorCode, (await res.JsonAsync()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task MissingPhotos_Gets409_IncompleteInspection_NamingTheArea()
    {
        var seed = await SeedAsync(complete: false);

        var res = await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = await res.JsonAsync();                       // đọc body một lần
        Assert.Equal("INCOMPLETE_INSPECTION", body.GetProperty("errorCode").GetString());
        Assert.Contains("Phòng ngủ", body.GetProperty("message").GetString());
        var inspection = await LoadAsync(seed.InspectionId);
        Assert.Equal(InspectionStatus.Open, inspection.Status);
        Assert.Null(inspection.TenantConfirmedAt);
    }

    [Fact]
    public async Task PhotosStillProcessing_Gets409_IncompleteInspection()
    {
        var seed = await SeedAsync(status: FileStatus.Received);
        await AssertError(await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed), HttpStatusCode.Conflict, "INCOMPLETE_INSPECTION");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task WithoutTickingTheCommitment_Gets400_ValidationError(bool? agreed)
    {
        var seed = await SeedAsync();
        await AssertError(await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), new { agreed }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task FirstConfirmation_AwaitsTheOtherParty_AndNotifiesThem()
    {
        var seed = await SeedAsync();

        var res = await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("AWAITING_CONFIRM", (await res.DataAsync()).GetProperty("status").GetString());
        var inspection = await LoadAsync(seed.InspectionId);
        Assert.NotNull(inspection.TenantConfirmedAt);
        Assert.Null(inspection.LandlordConfirmedAt);
        Assert.Equal(1, await AuditCount(seed.InspectionId, "INSPECTION_CONFIRMED"));
        Assert.Equal(1, EventCount("InspectionConfirmed", seed.InspectionId));     // notify gửi email cho bên còn lại
        Assert.Equal(0, EventCount("InspectionLocked", seed.InspectionId));
    }

    [Fact]
    public async Task SamePartyConfirmingTwice_DoesNotLock()
    {
        var seed = await SeedAsync();
        await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);

        var again = await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(InspectionStatus.AwaitingConfirm, (await LoadAsync(seed.InspectionId)).Status);
    }

    [Fact]
    public async Task SecondParty_Locks_AuditsAndPublishesInspectionLocked()
    {
        var seed = await SeedAsync();
        await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);

        var res = await seed.S.LandlordClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("LOCKED", (await res.DataAsync()).GetProperty("status").GetString());
        var inspection = await LoadAsync(seed.InspectionId);
        Assert.NotNull(inspection.LandlordConfirmedAt);
        Assert.NotNull(inspection.TenantConfirmedAt);
        Assert.Equal(1, await AuditCount(seed.InspectionId, "INSPECTION_LOCKED"));
        Assert.Equal(1, EventCount("InspectionLocked", seed.InspectionId));      // evidence-seal nghe sự kiện này
    }

    [Fact]
    public async Task BothPartiesAtTheSameMoment_LockExactlyOnce()
    {
        var seed = await SeedAsync();

        var results = await Task.WhenAll(
            seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed),
            seed.S.LandlordClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(InspectionStatus.Locked, (await LoadAsync(seed.InspectionId)).Status);
        Assert.Equal(1, await AuditCount(seed.InspectionId, "INSPECTION_LOCKED"));
        Assert.Equal(1, EventCount("InspectionLocked", seed.InspectionId));
    }

    [Fact]
    public async Task AlreadyLocked_Gets409_RecordLocked()
    {
        var seed = await SeedAsync();
        await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);
        await seed.S.LandlordClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);

        await AssertError(await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed), HttpStatusCode.Conflict, "RECORD_LOCKED");
    }

    [Fact]
    public async Task K1_AfterARealLock_PhotosCannotBeDeleted()
    {
        var seed = await SeedAsync();
        await seed.S.TenantClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);
        await seed.S.LandlordClient.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed);
        var photoId = await factory.WithDbAsync(db => db.Photos.Where(p => p.InspectionId == seed.InspectionId).Select(p => p.Id).FirstAsync());

        await AssertError(await seed.S.TenantClient.DeleteAsync($"/api/v1/photos/{photoId}"), HttpStatusCode.Conflict, "RECORD_LOCKED");
    }

    [Fact]
    public async Task Stranger_Gets404()
    {
        var seed = await SeedAsync();
        var stranger = factory.ClientFor(TestUser.Tenant("stranger@example.com"));
        await AssertError(await stranger.PostJsonAsync(ConfirmUrl(seed.InspectionId), Agreed), HttpStatusCode.NotFound, "NOT_FOUND");
    }
}
