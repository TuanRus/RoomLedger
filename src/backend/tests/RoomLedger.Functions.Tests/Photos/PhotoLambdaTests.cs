using Amazon.Lambda.CloudWatchEvents;
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
 * Lambda photo-ingest (S3 "Object Created" qua EventBridge → RECEIVED, EXIF, cảnh báo ảnh cũ, phát PhotoReceived) và
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

    /** Sự kiện "Object Created" mà S3 gửi qua EventBridge (bucket incoming bật EventBridge notifications). */
    private static CloudWatchEvent<S3ObjectCreatedDetail> ObjectCreated(string key) => new()
    {
        Source = "aws.s3",
        DetailType = "Object Created",
        Detail = new S3ObjectCreatedDetail(new S3BucketRef("roomledger-dev-incoming"), new S3ObjectRef(key, 1_000)),
    };

    [Fact]
    public void ObjectCreatedJson_FromEventBridge_DeserializesWithTheLambdaSerializer()
    {
        // Rút gọn từ ví dụ trong tài liệu S3 → EventBridge
        const string json = """
            {"version":"0","id":"17793124-05d4-b198-2fde-7ededc63b103","detail-type":"Object Created","source":"aws.s3",
             "account":"123456789012","time":"2026-11-08T03:00:00Z","region":"ap-southeast-2",
             "resources":["arn:aws:s3:::roomledger-dev-incoming"],
             "detail":{"version":"0","bucket":{"name":"roomledger-dev-incoming"},
                       "object":{"key":"a/b/c/d.heic","size":5,"etag":"b1946ac92492d2347c6235b4d2611184","sequencer":"00617F08299329D189"},
                       "request-id":"N4N7GDK58NMKJ12R","requester":"123456789012","reason":"PutObject"}}
            """;
        var serializer = new Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer();
        var evt = serializer.Deserialize<CloudWatchEvent<S3ObjectCreatedDetail>>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        Assert.Equal("roomledger-dev-incoming", evt.Detail.Bucket.Name);
        Assert.Equal("a/b/c/d.heic", evt.Detail.Object.Key);
    }

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
