using RoomLedger.Core.Common;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Services;

/**
 * Kết quả đánh giá chỉ số AI đọc: trạng thái và lý do cần kiểm tra (null nếu ổn).
 *
 * @author Tuấn
 * @version 1.0
 */
public record ReadingAssessment(MeterReadingStatus Status, string? ReviewReason);

/**
 * Luật chỉ số điện nước (BR-20, BR-21). Lambda persist-results (Sang) dùng lại Assess
 * để đánh dấu NeedsReview cùng một luật với API.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class MeterReadingService
{
    public const string ReasonLowConfidence = "LOW_CONFIDENCE";
    public const string ReasonLowerThanPrevious = "LOWER_THAN_PREVIOUS";
    public const string ReasonUnusualUsage = "UNUSUAL_USAGE";
    public const string ReasonAiFailed = "AI_FAILED";

    /**
     * Đánh dấu NEEDS_REVIEW khi AI không đọc được, confidence &lt; 0,8, chỉ số nhỏ hơn kỳ trước,
     * hoặc mức dùng &gt; 3 lần trung bình 3 kỳ trước (BR-20 RULE 3).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static ReadingAssessment Assess(long? aiValue, decimal? confidence, long? previousValue, IReadOnlyList<long> lastUsages)
    {
        if (aiValue is null)
        {
            return new(MeterReadingStatus.NeedsReview, ReasonAiFailed);
        }

        if (confidence is null or < TenancyService.ReviewConfidence)
        {
            return new(MeterReadingStatus.NeedsReview, ReasonLowConfidence);
        }

        if (previousValue is not null && aiValue < previousValue)
        {
            return new(MeterReadingStatus.NeedsReview, ReasonLowerThanPrevious);
        }

        if (previousValue is not null && lastUsages.Count > 0)
        {
            var average = lastUsages.Average();
            if (average > 0 && aiValue - previousValue > 3 * average)
            {
                return new(MeterReadingStatus.NeedsReview, ReasonUnusualUsage);
            }
        }

        return new(MeterReadingStatus.PendingConfirm, null);
    }

    /**
     * Kiểm chỉ số khi chủ nhà xác nhận (BR-21 RULE 1): không âm, không nhỏ hơn kỳ trước
     * trừ khi đánh dấu thay đồng hồ.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void ValidateConfirmedValue(long? value, long? previousValue, bool meterReplaced)
    {
        new Validator()
            .Require(value is >= 0, "value", "Chỉ số phải là số không âm")
            .Require(value is null || previousValue is null || meterReplaced || value >= previousValue, "value",
                "Chỉ số không được nhỏ hơn kỳ trước (đánh dấu thay đồng hồ nếu đúng)")
            .ThrowIfInvalid();
    }

    /**
     * Chỉ xác nhận chỉ số ở NEEDS_REVIEW hoặc PENDING_CONFIRM (BR-21 PRECONDITION).
     *
     * @author Tuấn
     * @version 1.0
     */
    public static void EnsureCanConfirm(MeterReadingStatus status)
    {
        if (status is not (MeterReadingStatus.NeedsReview or MeterReadingStatus.PendingConfirm))
        {
            throw new InvalidStateException("Chỉ số chưa đọc xong hoặc đã được xác nhận");
        }
    }
}
