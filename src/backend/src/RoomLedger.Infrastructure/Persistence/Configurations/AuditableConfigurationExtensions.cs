using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình chung cho khóa chính và cột audit của mọi bảng nghiệp vụ.
 *
 * @author Tuấn
 * @version 1.0
 */
public static class AuditableConfigurationExtensions
{
    /**
     * Khóa chính id do ứng dụng sinh (UUID v7), cột audit timestamptz.
     *
     * @author Tuấn
     * @version 1.0
     */
    public static EntityTypeBuilder<T> ConfigureAuditable<T>(this EntityTypeBuilder<T> b) where T : AuditableEntity
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.CreatedAt).IsRequired();
        b.Property(e => e.UpdatedAt).IsRequired();
        return b;
    }
}
