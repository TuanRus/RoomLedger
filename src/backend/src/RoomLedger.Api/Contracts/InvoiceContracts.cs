using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Body POST /properties/{propertyId}/invoices:generate (MD-05-FE-03).
 *
 * @author Tuấn
 * @version 1.0
 */
public record GenerateInvoicesRequest(string? Period);

/**
 * Hóa đơn vừa tạo trong lần lập hàng loạt.
 *
 * @author Tuấn
 * @version 1.0
 */
public record GeneratedInvoice(Guid InvoiceId, Guid RoomId, string RoomCode, long Total);

/**
 * Phòng bị bỏ qua khi lập hóa đơn và lý do (MISSING_READINGS, MISSING_PREVIOUS_READING, NO_LOCKED_CONTRACT, ALREADY_INVOICED).
 *
 * @author Tuấn
 * @version 1.0
 */
public record SkippedRoom(Guid RoomId, string RoomCode, string Reason);

/**
 * Kết quả lập hóa đơn hàng loạt.
 *
 * @author Tuấn
 * @version 1.0
 */
public record GenerateInvoicesResponse(IReadOnlyList<GeneratedInvoice> Created, IReadOnlyList<SkippedRoom> Skipped);

/**
 * Hóa đơn trong danh sách.
 *
 * @author Tuấn
 * @version 1.0
 */
public record InvoiceResponse(
    Guid InvoiceId,
    Guid TenancyId,
    string RoomCode,
    string PropertyName,
    string Period,
    InvoiceStatus Status,
    long Total,
    DateTimeOffset? SentAt,
    DateTimeOffset? PaidAt);

/**
 * Tiền một loại đồng hồ: chỉ số đầu, cuối, mức dùng, đơn giá, thành tiền, ảnh đồng hồ (presigned GET 5 phút).
 *
 * @author Tuấn
 * @version 1.0
 */
public record UtilityLine(Guid ReadingId, long Previous, long Current, long Usage, long Rate, long Amount, string? PhotoUrl);

/**
 * Khiếu nại chỉ số.
 *
 * @author Tuấn
 * @version 1.0
 */
public record DisputeResponse(
    Guid DisputeId,
    Guid InvoiceId,
    Guid ReadingId,
    string Reason,
    DisputeStatus Status,
    DisputeResolution? Resolution,
    string? Response,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

/**
 * Chi tiết hóa đơn (MD-05-FE-04).
 *
 * @author Tuấn
 * @version 1.0
 */
public record InvoiceDetailResponse(
    Guid InvoiceId,
    Guid TenancyId,
    string RoomCode,
    string PropertyName,
    string Period,
    InvoiceStatus Status,
    long RoomRent,
    UtilityLine Electricity,
    UtilityLine Water,
    long ServiceFee,
    long Total,
    DateTimeOffset? SentAt,
    DateTimeOffset? PaidAt,
    IReadOnlyList<DisputeResponse> Disputes);

/**
 * Body POST /invoices/{invoiceId}/disputes (người thuê khiếu nại chỉ số).
 *
 * @author Tuấn
 * @version 1.0
 */
public record CreateDisputeRequest(Guid? ReadingId, string? Reason);

/**
 * Body PATCH /disputes/{disputeId}: CORRECTED kèm value mới, hoặc KEPT kèm response.
 *
 * @author Tuấn
 * @version 1.0
 */
public record ResolveDisputeRequest(DisputeResolution? Resolution, long? Value, string? Response);
