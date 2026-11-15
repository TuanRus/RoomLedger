using ImageMagick;
using ImageMagick.Drawing;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Tests.Photos;

namespace RoomLedger.Functions.Tests.Compare;

/**
 * Dữ liệu mẫu cho luồng so ảnh trả phòng (MD-06-FE-02): một hợp đồng có biên bản nhận phòng LOCKED và biên bản trả
 * phòng (trạng thái tùy chọn), ba khu vực Phòng ngủ / Bếp / Phòng tắm theo thứ tự hiển thị.
 *
 * @author Sang
 * @version 1.0
 */
internal sealed class CompareScene(PhotoDbFixture db, FakeObjectStore store)
{
    public Guid TenancyId { get; private set; }
    public Guid MoveInId { get; private set; }
    public Guid MoveOutId { get; private set; }
    public Guid UploaderId { get; private set; }
    public Dictionary<string, Guid> Areas { get; } = new();

    public async Task<CompareScene> BuildAsync(InspectionStatus moveOutStatus = InspectionStatus.Comparing)
    {
        await using var ctx = db.NewDb();
        var landlordId = Guid.NewGuid();
        var property = new Property { LandlordId = landlordId, Name = "Nhà trọ Hoa Sen", Address = "12 Lê Lợi" };
        var room = new Room { Property = property, LandlordId = landlordId, Code = $"P.{Random.Shared.Next(100, 999)}", MonthlyRent = 3_000_000 };
        var sort = 0;
        foreach (var name in new[] { "Phòng ngủ", "Bếp", "Phòng tắm" })
        {
            var area = new RoomArea { RoomId = room.Id, Name = name, MinPhotos = 1, SortOrder = sort++ };
            room.Areas.Add(area);
            Areas[name] = area.Id;
        }
        var tenancy = new Tenancy { Room = room, LandlordId = landlordId, TenantId = Guid.NewGuid(), Status = TenancyStatus.MovingOut };
        var snapshot = InspectionService.SerializeSnapshot(InspectionService.BuildAreaSnapshot(room.Areas));
        var moveIn = new Inspection { Tenancy = tenancy, Type = InspectionType.MoveIn, Status = InspectionStatus.Locked, AreaSnapshotJson = snapshot };
        var moveOut = new Inspection { Tenancy = tenancy, Type = InspectionType.MoveOut, Status = moveOutStatus, AreaSnapshotJson = snapshot };
        ctx.Inspections.AddRange(moveIn, moveOut);
        await ctx.SaveChangesAsync();
        (TenancyId, MoveInId, MoveOutId, UploaderId) = (tenancy.Id, moveIn.Id, moveOut.Id, tenancy.TenantId!.Value);
        return this;
    }

    /**
     * Thêm một ảnh; thumbnail != null thì đặt thumbnail 400 px và bản xem trong bucket derived (bản xem = "view:" + id).
     */
    public async Task<Photo> AddPhotoAsync(Guid inspectionId, string area, FileStatus status, byte[]? thumbnail)
    {
        await using var ctx = db.NewDb();
        var photo = new Photo
        {
            InspectionId = inspectionId, RoomAreaId = Areas[area], UploadedBy = UploaderId, UploaderRole = UserRole.Tenant,
            FileName = "a.jpg", ContentType = "image/jpeg", Size = 1_000, Sha256 = Convert.ToBase64String(new byte[32]), Status = status,
        };
        photo.IncomingKey = $"seed/{TenancyId}/{inspectionId}/{photo.Id}.jpg";
        photo.DerivedKey = PhotoRules.ViewKey(photo.IncomingKey);
        if (thumbnail is not null)
        {
            store.Objects[(StorageBucket.Derived, PhotoRules.ThumbnailKey(photo.IncomingKey))] = (thumbnail, "image/jpeg");
            store.Objects[(StorageBucket.Derived, photo.DerivedKey)] = (System.Text.Encoding.UTF8.GetBytes("view:" + photo.Id), "image/jpeg");
        }
        ctx.Photos.Add(photo);
        await ctx.SaveChangesAsync();
        return photo;
    }
}

/** Ảnh "phòng" dựng bằng Magick.NET (giống PerceptualHashTests): mirrored đổi chỗ đồ đạc → khoảng cách lớn. */
internal static class RoomImages
{
    public static byte[] Room(bool mirrored = false)
    {
        using var img = new MagickImage(new MagickColor("#c8c4bc"), 400, 300);
        int X(int x) => mirrored ? 400 - x : x;
        new Drawables()
            .FillColor(new MagickColor("#3a3f4a")).Rectangle(Math.Min(X(25), X(190)), 190, Math.Max(X(25), X(190)), 275)
            .FillColor(new MagickColor("#e8f0ff")).Rectangle(Math.Min(X(250), X(375)), 40, Math.Max(X(250), X(375)), 140)
            .FillColor(new MagickColor("#7a5230")).Rectangle(Math.Min(X(205), X(245)), 110, Math.Max(X(205), X(245)), 275)
            .Draw(img);
        return img.ToByteArray(MagickFormat.Jpeg);
    }
}
