using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.S3Events;
using Amazon.Lambda.TestUtilities;
using ImageMagick;
using Microsoft.Extensions.Time.Testing;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Functions.Shared;
using Derive = RoomLedger.Functions.PhotoDerive.Function;
using Ingest = RoomLedger.Functions.PhotoIngest.Function;

namespace RoomLedger.Functions.Tests.Photos;

/**
 * Lambda photo-ingest (S3 ObjectCreated → RECEIVED, EXIF, cảnh báo ảnh cũ, phát PhotoReceived) và
 * photo-derive (PhotoReceived → bản xem JPEG + thumbnail trong bucket derived → READY). BR-16 RULE 4–5.
 *
 * @author Sang
 * @version 1.0
 */
public class PhotoLambdaTests(PhotoDbFixture db) : IClassFixture<PhotoDbFixture>
{
    private readonly FakeObjectStore _store = new();
    private readonly RecordingEvents _events = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero));

    private Ingest NewIngest() => new(db.NewDb, _store, _events, _clock, maxAgeDays: 3);
    private Derive NewDerive() => new(db.NewDb, _store);

    private static S3Event ObjectCreated(string key) => new()
    {
        Records =
        [
            new S3Event.S3EventNotificationRecord
            {
                EventName = "ObjectCreated:Put",
                S3 = new S3Event.S3Entity
                {
                    Bucket = new S3Event.S3BucketEntity { Name = "roomledger-dev-incoming" },
                    // S3 gửi key đã URL-encode
                    Object = new S3Event.S3ObjectEntity { Key = Uri.EscapeDataString(key).Replace("%2F", "/") },
                },
            },
        ],
    };

    private static CloudWatchEvent<PhotoEventDetail> Received(Guid photoId) =>
        new() { DetailType = PhotoEventDetail.PhotoReceived, Detail = new PhotoEventDetail(photoId) };

    [Fact]
    public async Task Ingest_MarksPhotoReceived_ReadsExif_FlagsOldPhoto_AndPublishesPhotoReceived()
    {
        var photo = await db.SeedPhotoAsync(FileStatus.PendingUpload);
        _store.Objects[(StorageBucket.Incoming, photo.IncomingKey)] = (SampleImages.Heic(), "image/heic");

        await NewIngest().HandleAsync(ObjectCreated(photo.IncomingKey), new TestLambdaContext());

        var stored = await db.ReloadAsync(photo.Id);
        Assert.Equal(FileStatus.Received, stored.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.FromHours(7)), stored.ExifTakenAt);
        Assert.True(stored.OldPhotoWarning);                          // chụp 19/10, nhận 8/11 > 3 ngày
        var published = Assert.Single(_events.Published);
        Assert.Equal(PhotoEventDetail.PhotoReceived, published.DetailType);
        Assert.Equal(photo.Id, ((PhotoEventDetail)published.Detail).PhotoId);
    }

    [Fact]
    public async Task Ingest_UnknownKey_ChangesNothing()
    {
        await NewIngest().HandleAsync(ObjectCreated($"x/y/z/{Guid.NewGuid()}.jpg"), new TestLambdaContext());
        await NewIngest().HandleAsync(ObjectCreated("not-a-photo.txt"), new TestLambdaContext());
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Ingest_SecondDeliveryForAReadyPhoto_DoesNothing()
    {
        var photo = await db.SeedPhotoAsync(FileStatus.Ready);
        _store.Objects[(StorageBucket.Incoming, photo.IncomingKey)] = (SampleImages.Heic(), "image/heic");

        await NewIngest().HandleAsync(ObjectCreated(photo.IncomingKey), new TestLambdaContext());

        Assert.Equal(FileStatus.Ready, (await db.ReloadAsync(photo.Id)).Status);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Ingest_RetryWhilePhotoIsStillReceived_PublishesAgain()
    {
        // Lần trước đã lưu RECEIVED nhưng phát sự kiện lỗi: lần chạy lại phải phát lại để photo-derive chạy
        var photo = await db.SeedPhotoAsync(FileStatus.Received);
        _store.Objects[(StorageBucket.Incoming, photo.IncomingKey)] = (SampleImages.Heic(), "image/heic");

        await NewIngest().HandleAsync(ObjectCreated(photo.IncomingKey), new TestLambdaContext());

        Assert.Single(_events.Published);
    }

    [Fact]
    public async Task Derive_WritesViewAndThumbnailToDerived_AndMarksPhotoReady()
    {
        var photo = await db.SeedPhotoAsync(FileStatus.Received);
        _store.Objects[(StorageBucket.Incoming, photo.IncomingKey)] = (SampleImages.Heic(), "image/heic");

        await NewDerive().HandleAsync(Received(photo.Id), new TestLambdaContext());

        var stored = await db.ReloadAsync(photo.Id);
        Assert.Equal(FileStatus.Ready, stored.Status);
        var viewKey = Path.ChangeExtension(photo.IncomingKey, ".jpg").Replace('\\', '/');
        Assert.Equal(viewKey, stored.DerivedKey);

        var view = _store.Objects[(StorageBucket.Derived, viewKey)];
        var thumb = _store.Objects[(StorageBucket.Derived, viewKey[..^4] + ".thumb.jpg")];
        Assert.Equal("image/jpeg", view.ContentType);
        using var thumbImage = new MagickImage(thumb.Content);
        Assert.Equal(400u, Math.Max(thumbImage.Width, thumbImage.Height));
        Assert.Null(new MagickImage(view.Content).GetExifProfile());  // không còn GPS
    }

    [Fact]
    public async Task Derive_PhotoNotReceived_DoesNothing()
    {
        var photo = await db.SeedPhotoAsync(FileStatus.PendingUpload);

        await NewDerive().HandleAsync(Received(photo.Id), new TestLambdaContext());

        Assert.Equal(FileStatus.PendingUpload, (await db.ReloadAsync(photo.Id)).Status);
        Assert.Empty(_store.Objects);
    }
}
