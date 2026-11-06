using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence.Configurations;

/**
 * Cấu hình bảng audit_event (chỉ ghi thêm).
 *
 * @author Tuấn
 * @version 1.0
 */
public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.EventType).HasMaxLength(50).IsRequired();
        b.Property(e => e.TargetType).HasMaxLength(50).IsRequired();
        b.Property(e => e.DetailJson).HasColumnType("jsonb");
        b.Property(e => e.TraceId).HasMaxLength(100);
        b.HasIndex(e => new { e.CreatedAt, e.Id }).HasDatabaseName("ix_audit_event_created_at");
        b.HasIndex(e => new { e.TargetType, e.TargetId }).HasDatabaseName("ix_audit_event_target_type_target_id");
        b.HasIndex(e => e.ActorId).HasDatabaseName("ix_audit_event_actor_id");
    }
}
