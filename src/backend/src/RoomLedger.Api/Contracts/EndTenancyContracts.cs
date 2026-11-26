using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Api.Contracts;

/**
 * Body POST /tenancies/{tenancyId}/end: bắt đầu (hoặc sửa) việc kết thúc hợp đồng. targetRoomId chỉ dùng khi kind = MOVE.
 *
 * @author Tuấn
 * @version 1.0
 */
public record EndTenancyRequest(DateOnly? EndDate, EndKind? Kind, Guid? TargetRoomId);

/**
 * Body POST /tenancies/{tenancyId}/end:finish: chốt hợp đồng. lastRent là tiền phòng kỳ cuối (không tính theo ngày);
 * startElectricity, startWater là chỉ số ban đầu ở phòng mới, bắt buộc khi kind = MOVE.
 *
 * @author Tuấn
 * @version 1.0
 */
public record FinishEndTenancyRequest(long? LastRent, long? StartElectricity, long? StartWater);

/**
 * Phòng chuyển đến (kind = MOVE).
 *
 * @author Tuấn
 * @version 1.0
 */
public record EndTargetRoomResponse(Guid RoomId, string Code, long MonthlyRent);

/**
 * Chỉ số cuối của một đồng hồ trong kỳ cuối. Mọi trường null khi chưa chụp ảnh. previous là chỉ số xác nhận kỳ trước
 * (hoặc chỉ số nhận phòng); value là số đã nhập hoặc số AI đọc.
 *
 * @author Tuấn
 * @version 1.0
 */
public record FinalReadingResponse(
    Guid? ReadingId,
    long? Previous,
    long? Value,
    MeterReadingStatus? Status,
    string? PhotoUrl,
    DateTimeOffset? ConfirmedAt,
    bool MeterReplaced = false);

/**
 * Đơn giá điện, nước dùng cho hóa đơn cuối.
 *
 * @author Tuấn
 * @version 1.0
 */
public record EndRatesResponse(long Electricity, long Water);

/**
 * Trạng thái kết thúc hợp đồng. unpaidPrevious: tổng hóa đơn SENT hoặc DISPUTED của hợp đồng (không gồm hóa đơn cuối),
 * hiển thị riêng, không cộng vào hóa đơn cuối. finalInvoiceId, endedAt, lastRent chỉ có sau khi chốt;
 * newTenancyId chỉ có khi MOVE đã chốt.
 *
 * @author Tuấn
 * @version 1.0
 */
public record EndTenancyResponse(
    Guid TenancyId,
    Guid RoomId,
    string RoomCode,
    string? TenantName,
    DateOnly EndDate,
    EndKind Kind,
    string FinalPeriod,
    EndTargetRoomResponse? TargetRoom,
    FinalReadingResponse Electricity,
    FinalReadingResponse Water,
    EndRatesResponse Rates,
    long UnpaidPrevious,
    long Deposit,
    long? LastRent,
    Guid? FinalInvoiceId,
    DateTimeOffset? EndedAt,
    Guid? NewTenancyId);

/**
 * Kết quả DELETE /tenancies/{tenancyId}/end: hợp đồng quay lại ACTIVE.
 *
 * @author Tuấn
 * @version 1.0
 */
public record CancelEndTenancyResponse(Guid TenancyId, TenancyStatus Status);
