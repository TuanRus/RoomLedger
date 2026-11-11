using System.Security.Cryptography;
using System.Text;
using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.TestUtilities;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Functions.Shared;
using RoomLedger.Functions.Tests.Photos;
using Seal = RoomLedger.Functions.EvidenceSeal.Function;

namespace RoomLedger.Functions.Tests.Evidence;

/**
 * Lambda evidence-seal (BR-18 RULE 4): sự kiện InspectionLocked → chép từng ảnh gốc READY từ incoming sang bucket
 * evidence (Object Lock), kiểm SHA-256 trước và sau khi chép, ghi EvidenceKey + ObjectVersionId, READY → SEALED.
 * Sai hash: không chép, ảnh giữ READY, ghi EVIDENCE_HASH_MISMATCH và ném lỗi để EventBridge thử lại / báo động.
 *
 * @author Sang
 * @version 1.0
 */
public class EvidenceSealTests(PhotoDbFixture db) : IClassFixture<PhotoDbFixture>
{
    private readonly FakeObjectStore _store = new();
    private Seal NewSeal() => new(db.NewDb, _store);

    private static string Sha(byte[] content) => Convert.ToBase64String(SHA256.HashData(content));

    private static CloudWatchEvent<InspectionLockedDetail> Locked(Guid inspectionId) =>
        new() { DetailType = "InspectionLocked", Detail = new InspectionLockedDetail(inspectionId, Guid.NewGuid()) };

    /** Biên bản ở trạng thái cho trước, mỗi ảnh có nội dung thật trong incoming; tamper = nội dung bị đổi so với hash đã ký. */
    private async Task<(Guid InspectionId, List<Photo> Photos)> SeedAsync(InspectionStatus status, int photos = 2, bool tamperFirst = false)
    {
        await using var ctx = db.NewDb();
        var landlordId = Guid.NewGuid();
        var property = new Property { LandlordId = landlordId, Name = "Nhà trọ Hoa Sen", Address = "12 Lê Lợi" };
        var room = new Room { Property = property, LandlordId = landlordId, Code = $"P.{Random.Shared.Next(100, 999)}", MonthlyRent = 3_000_000 };
        var area = new RoomArea { RoomId = room.Id, Name = "Phòng ngủ", MinPhotos = 1 };
        room.Areas.Add(area);
        var tenancy = new Tenancy { Room = room, LandlordId = landlordId, TenantId = Guid.NewGuid(), Status = TenancyStatus.Active };
        var inspection = new Inspection { Tenancy = tenancy, Type = InspectionType.MoveIn, Status = status };
        ctx.Inspections.Add(inspection);
        var list = new List<Photo>();
        for (var i = 0; i < photos; i++)
        {
            var content = Encoding.UTF8.GetBytes($"anh-goc-{Guid.NewGuid()}");
            var photo = new Photo
            {
                InspectionId = inspection.Id, RoomAreaId = area.Id, UploadedBy = tenancy.TenantId!.Value, UploaderRole = UserRole.Tenant,
                FileName = $"{i}.jpg", ContentType = "image/jpeg", Size = content.Length, Sha256 = Sha(content), Status = FileStatus.Ready,
            };
            photo.IncomingKey = $"{landlordId}/{tenancy.Id}/{inspection.Id}/{photo.Id}.jpg";
            var stored = tamperFirst && i == 0 ? Encoding.UTF8.GetBytes("da-bi-sua") : content;
            _store.Objects[(StorageBucket.Incoming, photo.IncomingKey)] = (stored, "image/jpeg");
            ctx.Photos.Add(photo);
            list.Add(photo);
        }
        await ctx.SaveChangesAsync();
        return (inspection.Id, list);
    }

    private async Task<List<Photo>> ReloadAsync(Guid inspectionId)
    {
        await using var ctx = db.NewDb();
        return await ctx.Photos.AsNoTracking().Where(p => p.InspectionId == inspectionId).ToListAsync();
    }

    private async Task<int> AuditCount(Guid inspectionId, string type)
    {
        await using var ctx = db.NewDb();
        return await ctx.AuditEvents.CountAsync(a => a.TargetId == inspectionId && a.EventType == type);
    }

    [Fact]
    public async Task LockedInspection_CopiesEveryReadyPhotoToEvidence_VerifiesHash_AndSeals()
    {
        var (inspectionId, photos) = await SeedAsync(InspectionStatus.Locked);

        await NewSeal().HandleAsync(Locked(inspectionId), new TestLambdaContext());

        foreach (var photo in await ReloadAsync(inspectionId))
        {
            Assert.Equal(FileStatus.Sealed, photo.Status);
            Assert.Equal(photo.IncomingKey, photo.EvidenceKey);                       // cùng bố cục khóa, khác bucket
            Assert.False(string.IsNullOrEmpty(photo.ObjectVersionId));
            Assert.Equal(photo.Sha256, Sha(_store.Objects[(StorageBucket.Evidence, photo.EvidenceKey!)].Content));
        }
        Assert.Equal(2, _store.CopyCount);
        Assert.Equal(1, await AuditCount(inspectionId, "EVIDENCE_SEALED"));
    }

    [Fact]
    public async Task RunningTwice_DoesNotCopyAgain()
    {
        var (inspectionId, _) = await SeedAsync(InspectionStatus.Locked);
        await NewSeal().HandleAsync(Locked(inspectionId), new TestLambdaContext());
        var versions = (await ReloadAsync(inspectionId)).Select(p => p.ObjectVersionId).ToList();

        await NewSeal().HandleAsync(Locked(inspectionId), new TestLambdaContext());   // EventBridge có thể gửi trùng

        Assert.Equal(2, _store.CopyCount);
        Assert.Equal(versions, (await ReloadAsync(inspectionId)).Select(p => p.ObjectVersionId).ToList());
    }

    [Fact]
    public async Task TamperedOriginal_IsNotCopied_StaysReady_IsAudited_AndTheHandlerFails()
    {
        var (inspectionId, photos) = await SeedAsync(InspectionStatus.Locked, tamperFirst: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewSeal().HandleAsync(Locked(inspectionId), new TestLambdaContext()));

        var after = (await ReloadAsync(inspectionId)).ToDictionary(p => p.Id);
        Assert.Equal(FileStatus.Ready, after[photos[0].Id].Status);
        Assert.Null(after[photos[0].Id].EvidenceKey);
        Assert.False(_store.Objects.ContainsKey((StorageBucket.Evidence, photos[0].IncomingKey)));   // không làm bẩn bucket Object Lock
        Assert.Equal(FileStatus.Sealed, after[photos[1].Id].Status);                                // ảnh còn lại vẫn được niêm phong
        Assert.Equal(1, await AuditCount(inspectionId, "EVIDENCE_HASH_MISMATCH"));
    }

    [Theory]
    [InlineData(InspectionStatus.Open)]
    [InlineData(InspectionStatus.AwaitingConfirm)]
    public async Task NotLocked_SealsNothing(InspectionStatus status)
    {
        var (inspectionId, _) = await SeedAsync(status);

        await NewSeal().HandleAsync(Locked(inspectionId), new TestLambdaContext());

        Assert.All(await ReloadAsync(inspectionId), p => Assert.Equal(FileStatus.Ready, p.Status));
        Assert.Equal(0, _store.CopyCount);
    }

    [Fact]
    public async Task UnknownInspection_DoesNothing()
    {
        await NewSeal().HandleAsync(Locked(Guid.NewGuid()), new TestLambdaContext());
        Assert.Equal(0, _store.CopyCount);
    }

    [Fact]
    public void InspectionLockedJson_FromTheApi_DeserializesWithTheLambdaSerializer()
    {
        // Dạng sự kiện API phát (EventBridgePublisher, camelCase) khi khóa biên bản
        var id = Guid.NewGuid();
        var json = $$$"""
            {"version":"0","id":"e1","detail-type":"InspectionLocked","source":"roomledger.api","account":"123456789012",
             "time":"2026-11-08T03:00:00Z","region":"ap-southeast-2","resources":[],
             "detail":{"inspectionId":"{{{id}}}","tenancyId":"{{{Guid.NewGuid()}}}","type":"MOVE_IN"}}
            """;
        var serializer = new Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer();
        var evt = serializer.Deserialize<CloudWatchEvent<InspectionLockedDetail>>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(id, evt.Detail.InspectionId);
    }
}
