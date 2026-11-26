using RoomLedger.Core.Common;
using RoomLedger.Core.Services;

namespace RoomLedger.Core.Tests;

/**
 * Test luật thông tin chuyển khoản: điền nội dung và kiểm mẫu.
 *
 * @author Tuấn
 * @version 1.0
 */
public class PaymentRulesTests
{
    [Theory]
    [InlineData(null, "P.204", "2026-11", "P204 T11")]
    [InlineData("{room} T{month}", "P 2.04", "2026-03", "P204 T03")]
    [InlineData("{room}-{period}-{year}", "A1", "2027-01", "A1-2027-01-2027")]
    [InlineData("Tien phong {room} thang {month}/{year}", "B.02", "2026-12", "Tien phong B02 thang 12/2026")]
    public void FillTransferNote_ReplacesPlaceholders(string? template, string room, string period, string expected) =>
        Assert.Equal(expected, PaymentRules.FillTransferNote(template, room, period));

    [Fact]
    public void ValidatePaymentInfo_AcceptsValidAndRejectsUnknownPlaceholder()
    {
        PaymentRules.ValidatePaymentInfo("VCB", "0123 456 789", "A", "{room} {month}");
        Assert.Throws<ValidationException>(() => PaymentRules.ValidatePaymentInfo("VCB", "0123456789", "A", "{tenant}"));
        Assert.Throws<ValidationException>(() => PaymentRules.ValidatePaymentInfo("VCB", "abc123456", "A", null));
    }

    [Fact]
    public void NormalizeAccountNumber_KeepsDigitsOnly() =>
        Assert.Equal("0123456789", PaymentRules.NormalizeAccountNumber(" 0123 456  789 "));
}
