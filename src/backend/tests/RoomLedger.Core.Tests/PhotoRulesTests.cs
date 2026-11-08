using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Quy tắc sau khi ảnh tới S3 (BR-16 RULE 4–5): cảnh báo ảnh cũ, key bản xem và thumbnail trong bucket derived.
 *
 * @author Sang
 * @version 1.0
 */
public class PhotoRulesTests
{
    private static readonly DateTimeOffset Received = new(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void IsOldPhoto_NoExifDate_IsNotFlagged()
        => Assert.False(PhotoRules.IsOldPhoto(null, Received, 3));

    [Fact]
    public void IsOldPhoto_TwoWeeksOld_IsFlagged()
        => Assert.True(PhotoRules.IsOldPhoto(Received.AddDays(-14), Received, 3));

    [Fact]
    public void IsOldPhoto_ExactlyThreeDays_IsNotFlagged_ButOneMinuteMoreIs()
    {
        Assert.False(PhotoRules.IsOldPhoto(Received.AddDays(-3), Received, 3));
        Assert.True(PhotoRules.IsOldPhoto(Received.AddDays(-3).AddMinutes(-1), Received, 3));
    }

    [Fact]
    public void IsOldPhoto_DateInTheFuture_IsNotFlagged()
        => Assert.False(PhotoRules.IsOldPhoto(Received.AddHours(5), Received, 3));

    [Theory]
    [InlineData("l/t/i/p.heic", "l/t/i/p.jpg", "l/t/i/p.thumb.jpg")]
    [InlineData("l/t/i/p.png", "l/t/i/p.jpg", "l/t/i/p.thumb.jpg")]
    [InlineData("l/t/i/p.jpg", "l/t/i/p.jpg", "l/t/i/p.thumb.jpg")]
    public void DerivedKeys_AreJpegNextToTheIncomingLayout(string incoming, string view, string thumb)
    {
        Assert.Equal(view, PhotoRules.ViewKey(incoming));
        Assert.Equal(thumb, PhotoRules.ThumbnailKey(incoming));
    }

    [Fact]
    public void PhotoIdFromKey_ReadsTheFileNameOfTheIncomingKey()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, PhotoRules.PhotoIdFromKey($"{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid()}/{id}.heic"));
        Assert.Null(PhotoRules.PhotoIdFromKey("random/upload.jpg"));
    }
}
