using RoomLedger.Core.Domain.Enums;

namespace RoomLedger.Core.Domain;

/**
 * Bảng property: nhà trọ của một chủ nhà, kèm giá điện nước mặc định (VND) và thông tin nhận chuyển khoản.
 * Mã QR nhận tiền: QrKey là file đang dùng (bucket derived, bền); QrPendingKey là file vừa xin link tải lên
 * (bucket incoming, tự xóa sau 7 ngày) và chỉ thay QrKey khi chủ nhà xác nhận đã tải xong,
 * nên QR cũ vẫn dùng được trong lúc tải bản mới.
 *
 * @author Tuấn
 * @version 1.2
 */
public class Property : AuditableEntity
{
    public Guid LandlordId { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public long ElectricityRate { get; set; }
    public long WaterRate { get; set; }
    public long ServiceFee { get; set; }
    public string? BankName { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? TransferNoteTemplate { get; set; }
    public string? QrKey { get; set; }
    public FileStatus? QrStatus { get; set; }
    public string? QrPendingKey { get; set; }
    /** Cột hệ thống xmin (concurrency token): xác nhận QR không ghi đè yêu cầu tải lên mới hơn. */
    public uint Version { get; set; }
    public List<Room> Rooms { get; set; } = [];
}
