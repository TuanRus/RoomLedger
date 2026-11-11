namespace RoomLedger.Core.Domain.Enums;

/**
 * Nguồn ảnh: chụp trực tiếp bằng camera trong app, hoặc chọn từ thư viện (ảnh cũ, có thể đã qua Zalo/Messenger
 * nên mất EXIF). Hai bên đều thấy nhãn nguồn ảnh (doc-fix-checklist E3/E4).
 *
 * @author Sang
 * @version 1.0
 */
public enum CaptureSource
{
    Camera,
    Gallery,
}
