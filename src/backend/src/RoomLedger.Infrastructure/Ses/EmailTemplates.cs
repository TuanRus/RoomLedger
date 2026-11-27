using System.Text.Json;

namespace RoomLedger.Infrastructure.Ses;

/** Một email đã dựng xong: tiêu đề và nội dung chữ thuần. */
public sealed record RenderedEmail(string Subject, string Body);

/**
 * Mẫu email theo templateId (trùng detail-type của sự kiện outbox). Chữ thuần tiếng Việt, không HTML: không có gì để
 * chèn mã, và email không chứa số tiền hay chỉ số chi tiết (xem trong ứng dụng). Giá trị lấy từ model (JSON của sự kiện)
 * chỉ dùng ở chỗ điền tên nhà trọ, mã phòng và đường dẫn.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class EmailTemplates
{
    public const string InviteCreated = "InviteCreated";
    public const string TenancyMoved = "TenancyMoved";
    public const string InvoiceSent = "InvoiceSent";
    public const string InvoiceDisputed = "InvoiceDisputed";
    public const string MeterReadingNeedsReview = "MeterReadingNeedsReview";

    /** Các templateId có mẫu; rule EventBridge của AiStack chỉ chuyển đúng những sự kiện này cho Lambda notify. */
    public static readonly IReadOnlyList<string> Supported =
    [
        InviteCreated, TenancyMoved, InvoiceSent, InvoiceDisputed, MeterReadingNeedsReview,
    ];

    /** Dựng email; templateId không có mẫu thì null. webOrigin là địa chỉ web app (không có dấu / cuối). */
    public static RenderedEmail? Render(string templateId, JsonElement model, string webOrigin)
    {
        var origin = webOrigin.TrimEnd('/');
        var place = Place(model);
        return templateId switch
        {
            InviteCreated => new RenderedEmail("Lời mời thuê phòng trên RoomLedger",
                $"Bạn được mời thuê {place}.\n\nMở liên kết sau để xem hợp đồng và nhận phòng:\n{origin}/invite/{Uri.EscapeDataString(Text(model, "token") ?? "")}\n\n" +
                "Nếu bạn không biết lời mời này, hãy bỏ qua email."),
            TenancyMoved => new RenderedEmail("Bạn được chuyển sang phòng mới",
                $"Chủ nhà đã chuyển bạn sang {place}.\n\nMở RoomLedger để xem hợp đồng mới: {origin}"),
            InvoiceSent => new RenderedEmail("Bạn có hóa đơn mới",
                $"Chủ nhà vừa gửi hóa đơn tiền phòng.\n\nXem chi tiết và thanh toán trong RoomLedger: {origin}"),
            InvoiceDisputed => new RenderedEmail("Người thuê khiếu nại hóa đơn",
                $"Người thuê vừa khiếu nại một hóa đơn.\n\nXem và trả lời trong RoomLedger: {origin}"),
            MeterReadingNeedsReview => new RenderedEmail("Có chỉ số điện nước cần kiểm tra",
                $"AI chưa chắc chắn khi đọc chỉ số {KindName(model)} {PeriodText(model)}.\n\nMở RoomLedger, xem ảnh và nhập hoặc xác nhận chỉ số: {origin}"),
            _ => null,
        };
    }

    private static string? Text(JsonElement model, string name) =>
        model.ValueKind == JsonValueKind.Object && model.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Place(JsonElement model) => (Text(model, "propertyName"), Text(model, "roomCode")) switch
    {
        ({ Length: > 0 } property, { Length: > 0 } room) => $"phòng {room} của {property}",
        (_, { Length: > 0 } room) => $"phòng {room}",
        _ => "một phòng trọ",
    };

    private static string KindName(JsonElement model) => Text(model, "kind") == "WATER" ? "nước" : "điện";

    private static string PeriodText(JsonElement model) => Text(model, "period") is { Length: > 0 } period ? $"kỳ {period}" : "của kỳ này";
}
