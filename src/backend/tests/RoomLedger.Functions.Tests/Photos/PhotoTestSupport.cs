using System.Collections.Concurrent;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Infrastructure.Persistence;

namespace RoomLedger.Functions.Tests.Photos;

/**
 * Database PostgreSQL tạm đã chạy migration (giống ApiFactory: ROOMLEDGER_TEST_DB), dùng quyền hệ thống như Lambda.
 *
 * @author Sang
 * @version 1.0
 */
public sealed class PhotoDbFixture : IAsyncLifetime
{
    private readonly string _admin;
    private readonly string _cs;
    private readonly string _database = "roomledger_test_" + Guid.NewGuid().ToString("N")[..12];

    public PhotoDbFixture()
    {
        var baseCs = Environment.GetEnvironmentVariable("ROOMLEDGER_TEST_DB")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
        _admin = new NpgsqlConnectionStringBuilder(baseCs) { Database = "postgres" }.ConnectionString;
        _cs = new NpgsqlConnectionStringBuilder(baseCs) { Database = _database }.ConnectionString;
    }

    public AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_cs).UseSnakeCaseNamingConvention().Options,
        SystemCurrentUser.Instance, TimeProvider.System);

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_admin);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_database} WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /** Tạo nhà, phòng, khu vực, hợp đồng, biên bản và một ảnh ở trạng thái cho trước; trả về ảnh. */
    public async Task<Photo> SeedPhotoAsync(FileStatus status, string ext = "heic")
    {
        await using var db = NewDb();
        var landlordId = Guid.NewGuid();
        var property = new Property { LandlordId = landlordId, Name = "Nhà trọ Hoa Sen", Address = "12 Lê Lợi" };
        var room = new Room { Property = property, LandlordId = landlordId, Code = $"P.{Random.Shared.Next(100, 999)}", MonthlyRent = 3_000_000 };
        var area = new RoomArea { RoomId = room.Id, Name = "Phòng ngủ", MinPhotos = 1 };
        room.Areas.Add(area);
        var tenancy = new Tenancy { Room = room, LandlordId = landlordId, TenantId = Guid.NewGuid(), Status = TenancyStatus.Active };
        var inspection = new Inspection { Tenancy = tenancy, Type = InspectionType.MoveIn };
        var photo = new Photo
        {
            InspectionId = inspection.Id, RoomAreaId = area.Id, UploadedBy = tenancy.TenantId!.Value, UploaderRole = UserRole.Tenant,
            FileName = "IMG." + ext, ContentType = "image/" + (ext == "jpg" ? "jpeg" : ext), Size = 1_000,
            Sha256 = Convert.ToBase64String(new byte[32]), Status = status, CaptureSource = CaptureSource.Camera,
        };
        photo.IncomingKey = $"{landlordId}/{tenancy.Id}/{inspection.Id}/{photo.Id}.{ext}";
        db.Inspections.Add(inspection);
        db.Photos.Add(photo);
        await db.SaveChangesAsync();
        return photo;
    }

    public async Task<Photo> ReloadAsync(Guid id)
    {
        await using var db = NewDb();
        return await db.Photos.AsNoTracking().SingleAsync(p => p.Id == id);
    }
}

/** S3 giả trong bộ nhớ. */
public sealed class FakeObjectStore : IObjectStore
{
    public ConcurrentDictionary<(StorageBucket Bucket, string Key), (byte[] Content, string ContentType)> Objects { get; } = new();

    public Task<Stream> OpenReadAsync(StorageBucket bucket, string key, CancellationToken ct) =>
        Objects.TryGetValue((bucket, key), out var o)
            ? Task.FromResult<Stream>(new MemoryStream(o.Content, writable: false))
            : throw new FileNotFoundException($"{bucket}/{key}");

    public Task PutAsync(StorageBucket bucket, string key, byte[] content, string contentType, CancellationToken ct)
    {
        Objects[(bucket, key)] = (content, contentType);
        return Task.CompletedTask;
    }
}

/** EventBridge giả: ghi lại sự kiện đã phát. */
public sealed class RecordingEvents : IEventPublisher
{
    public ConcurrentQueue<(string DetailType, object Detail)> Published { get; } = new();

    public Task PublishAsync(string detailType, object detail, CancellationToken ct)
    {
        Published.Enqueue((detailType, detail));
        return Task.CompletedTask;
    }
}

/** Ảnh mẫu cho test. */
public static class SampleImages
{
    /** HEIC tự tạo bằng pillow-heif: 4032x3024, EXIF DateTimeOriginal 2026:10:19 08:30:00, Orientation 6, có GPS. */
    public static byte[] Heic() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));

    /** JPEG 800x600 có EXIF ngày chụp, múi giờ +09:00 và GPS. */
    public static byte[] JpegWithGps()
    {
        using var img = new MagickImage(MagickColors.Orange, 800, 600);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, "2026:11:01 20:15:00");
        exif.SetValue(ExifTag.OffsetTimeOriginal, "+09:00");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(10, 1), new Rational(46, 1), new Rational(0, 1)]);
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        img.SetProfile(exif);
        return img.ToByteArray(MagickFormat.Jpeg);
    }

    /** PNG không có EXIF. */
    public static byte[] PngWithoutExif()
    {
        using var img = new MagickImage(MagickColors.Green, 300, 200);
        return img.ToByteArray(MagickFormat.Png);
    }
}
