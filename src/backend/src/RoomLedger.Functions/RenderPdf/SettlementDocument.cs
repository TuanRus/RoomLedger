using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Functions.RenderPdf;

/**
 * Biên bản thanh lý hợp đồng thuê phòng dạng PDF (MD-06-FE-05, BR-29), dựng bằng QuestPDF (giấy phép Community) với
 * font Lato đi kèm gói (đủ dấu tiếng Việt và ₫, không cần tải font). Nội dung: hai bên, nhà và phòng, kỳ trả phòng,
 * lúc quyết toán (giờ Việt Nam), từng khoản trừ cọc, từng hóa đơn chưa thanh toán và tổng kết; tiền trả lại âm được
 * ghi là "Người thuê còn phải trả" với số dương.
 *
 * @author Sang
 * @version 1.0
 */
public static class SettlementDocument
{
    private static readonly NumberFormatInfo VndFormat = new() { NumberGroupSeparator = ".", NumberGroupSizes = [3], NegativeSign = "-" };
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    static SettlementDocument()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /** Tiền VND: nhóm nghìn bằng dấu chấm, ví dụ 1.500.000 đ. */
    public static string Vnd(long amount) => amount.ToString("#,##0", VndFormat) + " đ";

    /** Dựng PDF A4 từ nội dung đã đọc. */
    public static byte[] Render(SettlementReport r) =>
        Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontFamily(Fonts.Lato).FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("BIÊN BẢN THANH LÝ HỢP ĐỒNG THUÊ PHÒNG").Bold().FontSize(16);
                    col.Item().AlignCenter().Text(
                        $"Kỳ trả phòng {r.FinalPeriod} · Quyết toán lúc {r.SettledAt.ToOffset(VietnamOffset):dd/MM/yyyy HH:mm}");
                    col.Item().PaddingBottom(10);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(12);
                    col.Item().Element(c => Parties(c, r));
                    col.Item().Element(c => Deductions(c, r));
                    col.Item().Element(c => Invoices(c, r));
                    col.Item().Element(c => Summary(c, r));
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span($"RoomLedger · Mã quyết toán {r.SettlementId} · Trang ");
                    t.CurrentPageNumber();
                    t.Span("/");
                    t.TotalPages();
                });
            }))
            .WithMetadata(new DocumentMetadata { Title = $"Biên bản thanh lý {r.RoomCode}", Author = "RoomLedger" })
            .GeneratePdf();

    private static void Parties(IContainer c, SettlementReport r) => c.Column(col =>
    {
        col.Item().Text("Thông tin").Bold().FontSize(12);
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(d => { d.ConstantColumn(90); d.RelativeColumn(); });
            foreach (var (label, value) in new[]
                     {
                         ("Nhà trọ", r.PropertyName), ("Địa chỉ", r.PropertyAddress), ("Phòng", r.RoomCode),
                         ("Chủ nhà", r.LandlordName), ("Người thuê", r.TenantName),
                     })
            {
                t.Cell().PaddingVertical(2).Text(label).SemiBold();
                t.Cell().PaddingVertical(2).Text(value);
            }
        });
    });

    private static void Deductions(IContainer c, SettlementReport r) => c.Column(col =>
    {
        col.Item().Text("Các khoản trừ cọc").Bold().FontSize(12);
        if (r.Deductions.Count == 0)
        {
            col.Item().Text("Không có khoản trừ cọc");
            return;
        }
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(d => { d.RelativeColumn(2); d.RelativeColumn(4); d.RelativeColumn(2); d.RelativeColumn(2); });
            t.Header(h =>
            {
                foreach (var title in new[] { "Khu vực", "Nội dung", "Trạng thái", "Số tiền" })
                {
                    h.Cell().Element(HeaderCell).Text(title).SemiBold();
                }
            });
            foreach (var line in r.Deductions)
            {
                t.Cell().Element(BodyCell).Text(line.Area);
                t.Cell().Element(BodyCell).Text(line.Title);
                t.Cell().Element(BodyCell).Text(line.Resolved ? "Đã xử lý khiếu nại" : "Đồng ý");
                t.Cell().Element(BodyCell).AlignRight().Text(Vnd(line.Amount));
            }
        });
    });

    private static void Invoices(IContainer c, SettlementReport r) => c.Column(col =>
    {
        col.Item().Text("Hóa đơn chưa thanh toán").Bold().FontSize(12);
        if (r.UnpaidInvoices.Count == 0)
        {
            col.Item().Text("Không có hóa đơn chưa thanh toán");
            return;
        }
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(d => { d.RelativeColumn(2); d.RelativeColumn(3); d.RelativeColumn(2); });
            t.Header(h =>
            {
                foreach (var title in new[] { "Kỳ", "Trạng thái", "Số tiền" })
                {
                    h.Cell().Element(HeaderCell).Text(title).SemiBold();
                }
            });
            foreach (var line in r.UnpaidInvoices)
            {
                t.Cell().Element(BodyCell).Text(line.Period);
                t.Cell().Element(BodyCell).Text(InvoiceStatusName(line.Status));
                t.Cell().Element(BodyCell).AlignRight().Text(Vnd(line.Total));
            }
        });
    });

    private static void Summary(IContainer c, SettlementReport r) => c.Column(col =>
    {
        col.Item().Text("Tổng kết").Bold().FontSize(12);
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(d => { d.RelativeColumn(3); d.RelativeColumn(2); });
            void Row(string label, long amount, bool bold = false)
            {
                var l = t.Cell().Element(BodyCell).Text(label);
                var v = t.Cell().Element(BodyCell).AlignRight().Text(Vnd(amount));
                if (bold)
                {
                    l.Bold();
                    v.Bold();
                }
            }
            Row("Tiền cọc", r.Deposit);
            Row("Trừ các khoản hư hỏng", r.DeductionsTotal);
            Row("Hóa đơn chưa thanh toán", r.UnpaidInvoicesTotal);
            Row(r.Refund >= 0 ? "Tiền trả lại người thuê" : "Người thuê còn phải trả", Math.Abs(r.Refund), bold: true);
        });
    });

    private static string InvoiceStatusName(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "Nháp",
        InvoiceStatus.Sent => "Đã gửi",
        InvoiceStatus.Disputed => "Đang khiếu nại",
        _ => "Đã thanh toán",
    };

    private static IContainer HeaderCell(IContainer c) =>
        c.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(3).PaddingHorizontal(2);

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(2);
}
