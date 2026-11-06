using RoomLedger.Core.Common;

namespace RoomLedger.Core.Domain;

/**
 * Lớp gốc của mọi bảng nghiệp vụ: khóa chính id (UUID v7) và 4 cột audit
 * created_at, updated_at, created_by, updated_by. Các cột audit do AppDbContext.SaveChangesAsync
 * tự gán, service không gán tay.
 *
 * @author Tuấn
 * @version 1.0
 */
public abstract class AuditableEntity
{
    public Guid Id { get; set; } = UuidV7.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
