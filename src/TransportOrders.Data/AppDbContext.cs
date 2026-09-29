using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TransportOrder> Orders => Set<TransportOrder>();

    public DbSet<Transport> Transports => Set<Transport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite has no date type: values come back with DateTimeKind.Unspecified, so mark them as UTC again.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

        modelBuilder.Entity<TransportOrder>(order =>
        {
            order.ToTable("TransportOrders");
            order.HasKey(o => o.Id);
            order.Property(o => o.OrderNumber).IsRequired().HasMaxLength(12);
            order.HasIndex(o => o.OrderNumber).IsUnique();
            order.Property(o => o.HeatNumber).IsRequired().HasMaxLength(30);
            order.HasIndex(o => o.HeatNumber);
            order.Property(o => o.PieceNumber).IsRequired().HasMaxLength(30);
            order.Property(o => o.ParentPieceNumber).HasMaxLength(30);
            order.Property(o => o.Material).IsRequired().HasMaxLength(100);
            order.Property(o => o.SourceLocation).IsRequired().HasMaxLength(100);
            order.Property(o => o.TargetLocation).IsRequired().HasMaxLength(100);
            order.Property(o => o.Priority).HasConversion<string>().HasMaxLength(10);
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.TransportNumber).HasMaxLength(12);
            order.Property(o => o.DueAt).HasConversion(utcConverter);
            order.Property(o => o.CreatedAt).HasConversion(utcConverter);
            order.Property(o => o.UpdatedAt).HasConversion(utcConverter);

            // The history is only reachable through the aggregate: EF writes the private list directly.
            order.HasMany(o => o.History)
                .WithOne()
                .HasForeignKey("TransportOrderId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            order.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);

            // An order refers to its transport by number; the database guarantees the transport exists.
            order.HasOne<Transport>()
                .WithMany()
                .HasForeignKey(o => o.TransportNumber)
                .HasPrincipalKey(t => t.TransportNumber)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StatusChange>(change =>
        {
            change.ToTable("StatusChanges");
            change.HasKey(c => c.Id);
            change.Property(c => c.FromStatus).HasConversion<string>().HasMaxLength(20);
            change.Property(c => c.ToStatus).HasConversion<string>().HasMaxLength(20);
            change.Property(c => c.ChangedAt).HasConversion(utcConverter);
            change.Property(c => c.ChangedBy).IsRequired().HasMaxLength(100);
            change.Property(c => c.Note).HasMaxLength(200);
        });

        modelBuilder.Entity<Transport>(transport =>
        {
            transport.ToTable("Transports");
            transport.HasKey(t => t.Id);
            // Unique through the alternate key the orders' foreign key points to.
            transport.Property(t => t.TransportNumber).IsRequired().HasMaxLength(12);
            transport.Property(t => t.Vehicle).HasConversion<string>().HasMaxLength(20);
            transport.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            transport.Property(t => t.PartLoadReason).HasConversion<string>().HasMaxLength(30);
            transport.Property(t => t.SourceLocation).IsRequired().HasMaxLength(100);
            transport.Property(t => t.TargetLocation).IsRequired().HasMaxLength(100);
            transport.Property(t => t.DispatchedBy).IsRequired().HasMaxLength(100);
            transport.Property(t => t.DispatchedAt).HasConversion(utcConverter);
            transport.Property(t => t.CompletedAt).HasConversion(nullableUtcConverter);
        });
    }
}
