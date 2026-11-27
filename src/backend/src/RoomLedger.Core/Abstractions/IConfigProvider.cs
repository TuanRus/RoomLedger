namespace RoomLedger.Core.Abstractions;

/**
 * Đọc cấu hình theo tên từ kho cấu hình (Parameter Store), không nằm trong code hay biến môi trường:
 * id mô hình AI, prompt. Đổi giá trị không cần deploy lại (có độ trễ do bộ nhớ đệm).
 *
 * @author Tuấn
 * @version 1.0
 */
public interface IConfigProvider
{
    /** Giá trị của cấu hình; ném lỗi nếu không có (không đoán giá trị mặc định). */
    Task<string> GetAsync(string name, CancellationToken ct);
}
