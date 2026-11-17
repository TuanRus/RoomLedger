using RoomLedger.Core.Domain.Enums;
using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Kỳ trả phòng và chỉ số tháng cuối (MD-06-FE-01, BR-25 RULE 3; quyết định A3, B1, B2 của Tuấn ngày 17/11).
 * Kỳ trả phòng là tháng (giờ Việt Nam, UTC+7) của lúc mở biên bản trả phòng. Gửi biên bản cần chỉ số điện và nước
 * của kỳ đó mà ảnh đồng hồ đã tới (khác PENDING_UPLOAD), hoặc chỉ số đã có số (qua bước PENDING_AI).
 *
 * @author Sang
 * @version 1.0
 */
public class MoveOutRulesTests
{
    [Theory]
    [InlineData("2026-11-30T16:59:59Z", "2026-11")]   // 23:59:59 ngày 30/11 giờ Việt Nam
    [InlineData("2026-11-30T17:00:00Z", "2026-12")]   // 00:00 ngày 01/12 giờ Việt Nam
    [InlineData("2026-12-31T17:00:00Z", "2027-01")]
    public void FinalPeriod_IsTheVietnamMonthOfOpening(string utc, string expected)
    {
        Assert.Equal(expected, InspectionService.FinalPeriod(DateTimeOffset.Parse(utc)));
    }

    [Fact]
    public void NoReadings_BothMetersMissing_ElectricityFirst()
    {
        Assert.Equal([MeterKind.Electricity, MeterKind.Water], InspectionService.MissingFinalReadings([]));
    }

    [Fact]
    public void PhotoArrived_Counts_EvenBeforeTheAiReadIt()
    {
        var missing = InspectionService.MissingFinalReadings([
            (MeterKind.Electricity, FileStatus.Received, MeterReadingStatus.PendingAi),
            (MeterKind.Water, FileStatus.Ready, MeterReadingStatus.PendingAi),
        ]);

        Assert.Empty(missing);
    }

    [Fact]
    public void UploadUrlRequestedButPhotoNeverArrived_IsMissing()
    {
        var missing = InspectionService.MissingFinalReadings([
            (MeterKind.Electricity, FileStatus.Received, MeterReadingStatus.PendingAi),
            (MeterKind.Water, FileStatus.PendingUpload, MeterReadingStatus.PendingAi),
        ]);

        Assert.Equal([MeterKind.Water], missing);
    }

    [Fact]
    public void ReadingWithoutPhotoStatus_ButAlreadyHasAValue_Counts()
    {
        var missing = InspectionService.MissingFinalReadings([
            (MeterKind.Electricity, null, MeterReadingStatus.Confirmed),
            (MeterKind.Water, FileStatus.PendingUpload, MeterReadingStatus.PendingConfirm),
        ]);

        Assert.Empty(missing);
    }

    [Fact]
    public void ReadingWithoutPhotoAndStillPendingAi_IsMissing()
    {
        Assert.Equal([MeterKind.Electricity], InspectionService.MissingFinalReadings([
            (MeterKind.Electricity, null, MeterReadingStatus.PendingAi),
            (MeterKind.Water, FileStatus.Sealed, MeterReadingStatus.PendingAi),
        ]));
    }

    [Fact]
    public void Message_NamesTheMissingMetersAndThePeriod()
    {
        Assert.Equal("Thiếu ảnh đồng hồ: điện, nước (kỳ 2026-11)",
            InspectionService.FinalReadingMissingMessage([MeterKind.Electricity, MeterKind.Water], "2026-11"));
        Assert.Equal("Thiếu ảnh đồng hồ: nước (kỳ 2026-12)",
            InspectionService.FinalReadingMissingMessage([MeterKind.Water], "2026-12"));
    }
}
