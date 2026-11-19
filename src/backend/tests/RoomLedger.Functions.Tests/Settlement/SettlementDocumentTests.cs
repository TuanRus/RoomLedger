using System.Text;
using RoomLedger.Core.Domain.Enums;
using RoomLedger.Functions.RenderPdf;
using UglyToad.PdfPig;

namespace RoomLedger.Functions.Tests.Settlement;

/**
 * Biên bản thanh lý hợp đồng (MD-06-FE-05, BR-29): PDF dựng bằng QuestPDF với font Lato đi kèm (đủ dấu tiếng Việt và ₫).
 * Đọc lại chữ bằng PdfPig để kiểm nội dung: hai bên, phòng, kỳ, từng khoản trừ cọc, hóa đơn chưa trả, tiền trả lại
 * (âm → "Người thuê còn phải trả"). Tiền VND dạng 1.500.000 đ, giờ Việt Nam.
 *
 * @author Sang
 * @version 1.0
 */
public class SettlementDocumentTests
{
    private static SettlementReport Report(long refund = 2_000_000, IReadOnlyList<DeductionLine>? deductions = null) => new(
        SettlementId: Guid.Parse("0193a1b2-0000-7000-8000-000000000001"),
        PropertyName: "Nhà trọ Hoa Sen",
        PropertyAddress: "12 Lê Lợi, Quận 1, TP Hồ Chí Minh",
        RoomCode: "P.204",
        LandlordName: "Nguyễn Văn Chủ",
        TenantName: "Trần Thị Thuê",
        FinalPeriod: "2026-11",
        SettledAt: new DateTimeOffset(2026, 11, 20, 3, 0, 0, TimeSpan.Zero),   // 10:00 giờ Việt Nam
        Deposit: 7_000_000,
        DeductionsTotal: 800_000,
        UnpaidInvoicesTotal: 7_000_000 - 800_000 - refund,
        Refund: refund,
        Deductions: deductions ??
        [
            new DeductionLine("Phòng ngủ", "Vết bẩn trên tường", 500_000, Resolved: false),
            new DeductionLine("Bếp", "Vỡ kính tủ bếp", 300_000, Resolved: true),
        ],
        UnpaidInvoices: [new InvoiceLine("2026-11", InvoiceStatus.Sent, 7_000_000 - 800_000 - refund)]);

    /** Chữ trong PDF, chuẩn hóa NFC và bỏ khoảng trắng (PdfPig tách từ theo vị trí, không giữ nguyên dấu cách). */
    private static string TextOf(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.Append(page.Text);
        }
        return Squash(sb.ToString());
    }

    private static string Squash(string s) => new string(s.Normalize(NormalizationForm.FormC).Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static void AssertHas(string text, params string[] expected)
    {
        foreach (var e in expected)
        {
            Assert.True(text.Contains(Squash(e)), $"PDF thiếu \"{e}\"");
        }
    }

    [Fact]
    public void Render_IsAPdf_WithTheTitle_Parties_RoomAndPeriod_InVietnamese()
    {
        var pdf = SettlementDocument.Render(Report());

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        AssertHas(TextOf(pdf),
            "BIÊN BẢN THANH LÝ HỢP ĐỒNG THUÊ PHÒNG",
            "Nhà trọ Hoa Sen", "12 Lê Lợi, Quận 1, TP Hồ Chí Minh", "P.204",
            "Nguyễn Văn Chủ", "Trần Thị Thuê",
            "2026-11", "20/11/2026 10:00");
    }

    [Fact]
    public void Render_ListsEachDeduction_AndEachUnpaidInvoice_WithVndAmounts()
    {
        var text = TextOf(SettlementDocument.Render(Report()));

        AssertHas(text,
            "Phòng ngủ", "Vết bẩn trên tường", "500.000 đ", "Đồng ý",
            "Bếp", "Vỡ kính tủ bếp", "300.000 đ", "Đã xử lý khiếu nại",
            "Đã gửi", "4.200.000 đ",
            "Tiền cọc", "7.000.000 đ", "800.000 đ");
    }

    [Fact]
    public void PositiveRefund_SaysWhatIsReturnedToTheTenant()
    {
        AssertHas(TextOf(SettlementDocument.Render(Report(refund: 2_000_000))), "Tiền trả lại người thuê", "2.000.000 đ");
    }

    [Fact]
    public void NegativeRefund_SaysWhatTheTenantStillOwes_WithoutAMinusSign()
    {
        var text = TextOf(SettlementDocument.Render(Report(refund: -2_800_000)));

        AssertHas(text, "Người thuê còn phải trả", "2.800.000 đ");
        Assert.DoesNotContain(Squash("Tiền trả lại người thuê"), text);
        Assert.DoesNotContain("-2.800.000", text);
    }

    [Fact]
    public void NoDeductions_SaysSo()
    {
        AssertHas(TextOf(SettlementDocument.Render(Report(deductions: []))), "Không có khoản trừ cọc");
    }

    [Theory]
    [InlineData(0, "0 đ")]
    [InlineData(1_500_000, "1.500.000 đ")]
    [InlineData(-2_800_000, "-2.800.000 đ")]
    public void Vnd_UsesDotGrouping_AndTheDongSign(long amount, string expected)
    {
        Assert.Equal(expected, SettlementDocument.Vnd(amount));
    }
}
