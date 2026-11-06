using Microsoft.EntityFrameworkCore;
using RoomLedger.Core.Domain;

namespace RoomLedger.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Tenancy> Tenancies => Set<Tenancy>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mỗi entity một file IEntityTypeConfiguration trong Configurations/
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        // TODO: global query filter theo landlord_id / tenant_id của ICurrentUser
    }
}
