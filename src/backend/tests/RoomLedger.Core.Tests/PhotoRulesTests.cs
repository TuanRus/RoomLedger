using RoomLedger.Core.Domain.Enums;
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

    // K2 (Phát, 15/11): ảnh tới sau khi biên bản đã khóa hoặc đã gửi so sánh thì không được nhận
    [Theory]
    [InlineData(InspectionType.MoveIn, InspectionStatus.Open, true)]
    [InlineData(InspectionType.MoveIn, InspectionStatus.AwaitingConfirm, true)]
    [InlineData(InspectionType.MoveIn, InspectionStatus.Locked, false)]
    [InlineData(InspectionType.MoveIn, InspectionStatus.Settled, false)]
    [InlineData(InspectionType.MoveOut, InspectionStatus.Open, true)]
    [InlineData(InspectionType.MoveOut, InspectionStatus.Comparing, false)]
    [InlineData(InspectionType.MoveOut, InspectionStatus.Reviewing, false)]
    [InlineData(InspectionType.MoveOut, InspectionStatus.AwaitingConfirm, false)]   // trả phòng: đã gửi người thuê
    [InlineData(InspectionType.MoveOut, InspectionStatus.Settled, false)]
    public void AcceptsPhotos_OnlyWhileTheInspectionIsStillOpenForPhotos(InspectionType type, InspectionStatus status, bool expected)
        => Assert.Equal(expected, PhotoRules.AcceptsPhotos(type, status));
}
