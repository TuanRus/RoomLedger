using Amazon.Lambda.CloudWatchEvents;
using Amazon.Lambda.TestUtilities;
using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Domain;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;
using RoomLedger.Functions.Shared;
using RoomLedger.Functions.Tests.Photos;
using MeterFunction = RoomLedger.Functions.MeterResult.Function;

namespace RoomLedger.Functions.Tests.Ai;

/**
 * Lambda meter-prepare / meter-persist (trong VPC): chọn ảnh đồng hồ chờ AI và ghi kết quả theo BR-20.
 * Cần PostgreSQL (ROOMLEDGER_TEST_DB), giống test photo.
 *
 * @author Tuấn
 * @version 1.0
 */
public class MeterResultTests(PhotoDbFixture db) : IClassFixture<PhotoDbFixture>
{
    private readonly RecordingEvents _events = new();

    private MeterFunction NewFunction() => new(db.NewDb, _events);

    private sealed record Seeded(Guid ReadingId, Guid TenancyId, Guid RoomId, Guid LandlordId, string Key);

    /** Phòng + hợp đồng có chỉ số ban đầu (điện 1000), một chỉ số đang chờ AI với ảnh đã tải lên. */
    private async Task<Seeded> SeedAsync(MeterKind kind = MeterKind.Electricity, MeterReadingStatus status = MeterReadingStatus.PendingAi,
        FileStatus photo = FileStatus.PendingUpload, long? opening = 1000, string period = "2026-11")
    {
        await using var ctx = db.NewDb();
        var landlordId = Guid.NewGuid();
        var property = new Property { LandlordId = landlordId, Name = "Nhà trọ Hoa Sen", Address = "12 Lê Lợi" };
        var room = new Room { Property = property, LandlordId = landlordId, Code = $"P.{Random.Shared.Next(100, 999)}", MonthlyRent = 3_000_000 };
        var tenancy = new Tenancy
        {
            Room = room, LandlordId = landlordId, TenantId = Guid.NewGuid(), Status = TenancyStatus.Active,
            OpeningElectricity = opening, OpeningWater = opening,
        };
        var reading = new MeterReading { RoomId = room.Id, TenancyId = tenancy.Id, Period = period, Kind = kind, Status = status, PhotoStatus = photo };
        reading.PhotoKey = $"{landlordId}/{tenancy.Id}/{reading.Id}/{Guid.NewGuid()}.jpg";
        ctx.Tenancies.Add(tenancy);
        ctx.MeterReadings.Add(reading);
        await ctx.SaveChangesAsync();
        return new Seeded(reading.Id, tenancy.Id, room.Id, landlordId, reading.PhotoKey);
    }

    private async Task<MeterReading> ReloadAsync(Guid id)
    {
        await using var ctx = db.NewDb();
        return await ctx.MeterReadings.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private static CloudWatchEvent<S3ObjectCreatedDetail> Created(string key) => new()
    {
        Detail = new S3ObjectCreatedDetail(new S3BucketRef("roomledger-dev-incoming"), new S3ObjectRef(key, 1_000)),
    };

    private static MeterPersistInput Ok(Seeded s, long value, decimal confidence = 0.95m, string? key = null) =>
        new(s.ReadingId, key ?? s.Key, true, value, confidence, "");

    [Theory]
    [InlineData(MeterKind.Electricity, "readElectricMeter")]
    [InlineData(MeterKind.Water, "readWaterMeter")]
    public async Task Prepare_PendingMeterPhoto_ReturnsTheTaskForItsKind_AndMarksThePhotoReady(MeterKind kind, string task)
    {
        var s = await SeedAsync(kind);

        var result = await NewFunction().PrepareAsync(Created(s.Key), new TestLambdaContext());

        Assert.Equal(new MeterPrepareResult(true, s.ReadingId, s.Key, task), result);
        Assert.Equal(FileStatus.Ready, (await ReloadAsync(s.ReadingId)).PhotoStatus);
    }

    [Fact]
    public async Task Prepare_UrlEncodedKeyFromS3_IsDecoded()
    {
        var s = await SeedAsync();
        var result = await NewFunction().PrepareAsync(Created(s.Key.Replace("/", "%2F")), new TestLambdaContext());
        Assert.True(result.Found);
    }

    [Fact]
    public async Task Prepare_PhotoThatIsNotAMeterPhoto_IsIgnored()
    {
        var result = await NewFunction().PrepareAsync(Created("a/b/c/d.heic"), new TestLambdaContext());
        Assert.Equal(new MeterPrepareResult(false), result);
    }

    [Theory]
    [InlineData(MeterReadingStatus.PendingConfirm)]
    [InlineData(MeterReadingStatus.Confirmed)]
    public async Task Prepare_ReadingAlreadyHandled_IsIgnored(MeterReadingStatus status)
    {
        var s = await SeedAsync(status: status);
        Assert.False((await NewFunction().PrepareAsync(Created(s.Key), new TestLambdaContext())).Found);
    }

    [Fact]
    public async Task Persist_ConfidentReadingAbovePrevious_WaitsForTheLandlordToConfirm()
    {
        var s = await SeedAsync();

        await NewFunction().PersistAsync(Ok(s, 1250), new TestLambdaContext());

        var r = await ReloadAsync(s.ReadingId);
        Assert.Equal(MeterReadingStatus.PendingConfirm, r.Status);
        Assert.Equal(1250, r.AiValue);
        Assert.Equal(0.95m, r.AiConfidence);
        Assert.Null(r.ReviewReason);
        Assert.Contains("1250", r.AiRawJson);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Persist_LowConfidence_NeedsReview_AndTellsTheLandlord()
    {
        var s = await SeedAsync();

        await NewFunction().PersistAsync(Ok(s, 1250, 0.5m), new TestLambdaContext());

        var r = await ReloadAsync(s.ReadingId);
        Assert.Equal(MeterReadingStatus.NeedsReview, r.Status);
        Assert.Equal(MeterReadingService.ReasonLowConfidence, r.ReviewReason);
        var (type, detail) = Assert.Single(_events.Published);
        Assert.Equal(MeterFunction.NeedsReviewEvent, type);
        var json = System.Text.Json.JsonSerializer.Serialize(detail);
        Assert.Contains(s.LandlordId.ToString(), json);
        Assert.Contains("LOW_CONFIDENCE", json);
    }

    [Fact]
    public async Task Persist_ValueBelowThePreviousReading_NeedsReview()
    {
        var s = await SeedAsync(opening: 5000);
        await NewFunction().PersistAsync(Ok(s, 4000), new TestLambdaContext());
        Assert.Equal(MeterReadingService.ReasonLowerThanPrevious, (await ReloadAsync(s.ReadingId)).ReviewReason);
    }

    [Fact]
    public async Task Persist_UsageOverThreeTimesTheRecentAverage_NeedsReview()
    {
        var s = await SeedAsync(opening: null);
        await using (var ctx = db.NewDb())
        {
            // Ba kỳ trước đã xác nhận: 1000 → 1100 → 1200 → 1300 (mức dùng 100 mỗi kỳ)
            var tenancy = await ctx.Tenancies.AsNoTracking().SingleAsync(t => t.Id == s.TenancyId);
            string[] periods = ["2026-07", "2026-08", "2026-09", "2026-10"];
            for (var i = 0; i < periods.Length; i++)
            {
                ctx.MeterReadings.Add(new MeterReading
                {
                    RoomId = s.RoomId, TenancyId = s.TenancyId, Period = periods[i], Kind = MeterKind.Electricity,
                    Status = MeterReadingStatus.Confirmed, Value = 1000 + i * 100,
                });
            }
            await ctx.SaveChangesAsync();
        }

        await NewFunction().PersistAsync(Ok(s, 1900), new TestLambdaContext());   // 600 > 3 × 100

        var r = await ReloadAsync(s.ReadingId);
        Assert.Equal(MeterReadingStatus.NeedsReview, r.Status);
        Assert.Equal(MeterReadingService.ReasonUnusualUsage, r.ReviewReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Persist_AiFailedOrUnreadable_BecomesNeedsReview_SoTheLandlordCanTypeIt(bool withError)
    {
        var s = await SeedAsync();
        var input = withError
            ? new MeterPersistInput(s.ReadingId, s.Key, Error: "Bedrock timeout")
            : new MeterPersistInput(s.ReadingId, s.Key, false, null, 0.1m, "Ảnh mờ");

        await NewFunction().PersistAsync(input, new TestLambdaContext());

        var r = await ReloadAsync(s.ReadingId);
        Assert.Equal(MeterReadingStatus.NeedsReview, r.Status);
        Assert.Equal(MeterReadingService.ReasonAiFailed, r.ReviewReason);
        Assert.Null(r.AiValue);
        Assert.Single(_events.Published);
    }

    [Fact]
    public async Task Persist_ResultForAnOlderPhoto_DoesNotOverwriteTheNewerOne()
    {
        var s = await SeedAsync();
        await NewFunction().PersistAsync(Ok(s, 1250, key: "old/photo.jpg"), new TestLambdaContext());
        var r = await ReloadAsync(s.ReadingId);
        Assert.Equal(MeterReadingStatus.PendingAi, r.Status);
        Assert.Null(r.AiValue);
    }

    [Fact]
    public async Task Persist_Twice_DoesNotRunOverALandlordsLaterEdit()
    {
        var s = await SeedAsync();
        await NewFunction().PersistAsync(Ok(s, 1250), new TestLambdaContext());
        await using (var ctx = db.NewDb())
        {
            var r = await ctx.MeterReadings.SingleAsync(m => m.Id == s.ReadingId);
            r.Value = 1260;
            r.Status = MeterReadingStatus.Confirmed;
            await ctx.SaveChangesAsync();
        }

        await NewFunction().PersistAsync(Ok(s, 9999), new TestLambdaContext());

        var after = await ReloadAsync(s.ReadingId);
        Assert.Equal(1250, after.AiValue);
        Assert.Equal(1260, after.Value);
        Assert.Equal(MeterReadingStatus.Confirmed, after.Status);
    }
}
