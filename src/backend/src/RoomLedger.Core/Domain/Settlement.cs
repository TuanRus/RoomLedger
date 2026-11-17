namespace RoomLedger.Core.Domain;

/**
 * Bảng settlement: quyết toán trả phòng của một hợp đồng (MD-06-FE-05, BR-29), mỗi hợp đồng một dòng (unique
 * tenancy_id). Số tiền là bigint VND, chốt lúc quyết toán: Deposit (cọc của phiên bản hợp đồng khóa mới nhất),
 * DeductionsTotal (mục ACCEPTED + RESOLVED), UnpaidInvoicesTotal (mọi hóa đơn chưa PAID, gồm hóa đơn cuối),
 * Refund = cọc − trừ − chưa trả (âm: người thuê còn nợ). PdfKey: biên bản thanh lý trong bucket derived (reports/),
 * null tới khi tạo xong. Thiết kế đã được Tuấn duyệt (quyết định A2, 17/11).
 *
 * @author Sang
 * @version 1.0
 */
public class Settlement : AuditableEntity
{
    public Guid TenancyId { get; set; }
    public Guid InspectionId { get; set; }
    public long Deposit { get; set; }
    public long DeductionsTotal { get; set; }
    public long UnpaidInvoicesTotal { get; set; }
    public long Refund { get; set; }
    public string? PdfKey { get; set; }
    public DateTimeOffset SettledAt { get; set; }
    public Guid SettledBy { get; set; }
    public Tenancy? Tenancy { get; set; }
}
