using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Abstractions;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence;

/**
 * DbContext dùng chung của RoomLedger (RDS PostgreSQL, EF Core code first, snake_case).
 * - Gán cột audit trong SaveChangesAsync.
 * - Global query filter theo chủ sở hữu: chủ nhà thấy bản ghi của mình, người thuê thấy hợp đồng của mình.
 *   Lambda nền (ICurrentUser.IsSystem) không bị lọc. Request chưa đăng nhập dùng Guid.Empty nên không thấy gì.
 * - Enum lưu UPPER_SNAKE_CASE.
 *
 * @author Tuấn
 * @version 1.1
 */
public class AppDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly bool _isSystem;
    private readonly Guid _userId;

    /**
     * Tạo DbContext cho một request hoặc một lần chạy Lambda.
     *
     * @author Tuấn
     * @version 1.1
     */
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser, TimeProvider clock) : base(options)
    {
        _currentUser = currentUser;
        _clock = clock;
        _isSystem = currentUser.IsSystem;
        _userId = currentUser.IsAuthenticated ? currentUser.UserId : Guid.Empty;
    }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomArea> RoomAreas => Set<RoomArea>();
    public DbSet<Tenancy> Tenancies => Set<Tenancy>();
    public DbSet<TenancyVersion> TenancyVersions => Set<TenancyVersion>();
    public DbSet<TenancyFile> TenancyFiles => Set<TenancyFile>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /** Thời điểm hiện tại theo đồng hồ của context (test thay được). */
    public DateTimeOffset Now => _clock.GetUtcNow();

    /**
     * Ghi audit_event trong cùng transaction với thay đổi nghiệp vụ.
     *
     * @author Tuấn
     * @version 1.0
     */
    public void Audit(string eventType, string targetType, Guid targetId, string? detailJson = null, string? traceId = null)
    {
        AuditEvents.Add(new AuditEvent
        {
            EventType = eventType,
            ActorId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            TargetType = targetType,
            TargetId = targetId,
            DetailJson = detailJson,
            TraceId = traceId,
            CreatedAt = Now,
        });
    }

    /**
     * Gán created_at, updated_at, created_by, updated_by trước khi lưu. Service không gán tay các cột này.
     *
     * @author Tuấn
     * @version 1.0
     */
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditColumns();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /**
     * Bản đồng bộ cũng gán cột audit để không có đường lưu nào bỏ sót.
     *
     * @author Tuấn
     * @version 1.0
     */
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditColumns();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mỗi entity một file IEntityTypeConfiguration trong Configurations/
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyTableNames(modelBuilder);
        ApplyEnumConventions(modelBuilder);
        ApplyOwnerFilters(modelBuilder);
    }

    private void ApplyOwnerFilters(ModelBuilder b)
    {
        // Người thuê chỉ thấy nhà, phòng có hợp đồng của mình; filter của Room đi qua Tenancy (không vòng lặp)
        b.Entity<Property>().HasQueryFilter(p => _isSystem || p.LandlordId == _userId
            || p.Rooms.Any(r => r.Tenancies.Any(t => t.TenantId == _userId)));
        b.Entity<Room>().HasQueryFilter(r => _isSystem || r.LandlordId == _userId
            || r.Tenancies.Any(t => t.TenantId == _userId));
        b.Entity<Tenancy>().HasQueryFilter(t => _isSystem || t.LandlordId == _userId || t.TenantId == _userId);
        b.Entity<TenancyVersion>().HasQueryFilter(v => _isSystem
            || v.Tenancy!.LandlordId == _userId || v.Tenancy.TenantId == _userId);
        b.Entity<Inspection>().HasQueryFilter(i => _isSystem
            || i.Tenancy!.LandlordId == _userId || i.Tenancy.TenantId == _userId);
        b.Entity<Finding>().HasQueryFilter(f => _isSystem
            || f.Inspection!.Tenancy!.LandlordId == _userId || f.Inspection.Tenancy.TenantId == _userId);
        b.Entity<MeterReading>().HasQueryFilter(m => _isSystem
            || m.Tenancy!.LandlordId == _userId || m.Tenancy.TenantId == _userId);
        b.Entity<Invoice>().HasQueryFilter(i => _isSystem
            || i.Tenancy!.LandlordId == _userId || i.Tenancy.TenantId == _userId);
        b.Entity<Dispute>().HasQueryFilter(d => _isSystem
            || d.Tenancy!.LandlordId == _userId || d.Tenancy.TenantId == _userId);
        b.Entity<Invite>().HasQueryFilter(i => _isSystem || i.Tenancy!.LandlordId == _userId);
        b.Entity<AppUser>().HasQueryFilter(u => _isSystem || u.Id == _userId);
    }

    private static void ApplyTableNames(ModelBuilder b)
    {
        // Tên bảng snake_case số ít, trùng tên entity (AppUser → app_user), không lấy theo tên DbSet số nhiều
        foreach (var entity in b.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            entity.SetTableName(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(entity.ClrType.Name));
        }
    }

    private static void ApplyEnumConventions(ModelBuilder b)
    {
        foreach (var property in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (!type.IsEnum)
            {
                continue;
            }

            var converterType = typeof(UpperSnakeEnumConverter<>).MakeGenericType(type);
            property.SetValueConverter((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)Activator.CreateInstance(converterType)!);
            property.SetMaxLength(40);
        }
    }

    private void StampAuditColumns()
    {
        var now = Now;
        Guid? actor = _currentUser.IsAuthenticated ? _currentUser.UserId : null;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
                entry.Entity.CreatedBy = actor;
                entry.Entity.UpdatedBy = actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(e => e.CreatedAt).IsModified = false;
                entry.Property(e => e.CreatedBy).IsModified = false;
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = actor;
            }
        }
    }
}
