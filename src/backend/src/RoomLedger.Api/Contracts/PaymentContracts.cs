using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Body PUT /properties/{propertyId}/payment-info. transferNoteTemplate bỏ trống thì dùng mẫu mặc định "{room} T{month}".
 *
 * @author Tuấn
 * @version 1.0
 */
public record UpdatePaymentInfoRequest(string? BankName, string? AccountNumber, string? AccountHolder, string? TransferNoteTemplate);

/**
 * Thông tin chuyển khoản của nhà trọ cho chủ nhà. qrUrl là presigned GET 5 phút, chỉ có khi QR đã tải xong;
 * qrStatus là RECEIVED khi đã có QR, PENDING_UPLOAD khi mới xin link tải lên mà chưa xác nhận.
 *
 * @author Tuấn
 * @version 1.0
 */
public record PaymentInfoResponse(
    string? BankName,
    string? AccountNumber,
    string? AccountHolder,
    string TransferNoteTemplate,
    string? QrUrl,
    FileStatus? QrStatus);

/**
 * Thông tin chuyển khoản trên hóa đơn: transferNote là mẫu đã điền cho hóa đơn đó (ví dụ "P204 T11").
 *
 * @author Tuấn
 * @version 1.0
 */
public record PaymentResponse(string BankName, string AccountNumber, string AccountHolder, string TransferNote, string? QrUrl);

/**
 * Body PATCH /properties/{propertyId}: đổi đơn giá mặc định của nhà trọ. Chỉ gửi trường cần đổi.
 * Giá mới áp cho hóa đơn lập sau đó; hóa đơn đã lập giữ nguyên.
 *
 * @author Tuấn
 * @version 1.0
 */
public record UpdatePropertyRequest(long? ElectricityRate, long? WaterRate, long? ServiceFee);
