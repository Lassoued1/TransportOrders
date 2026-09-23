using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TransportOrder> Orders => Set<TransportOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite has no date type: values come back with DateTimeKind.Unspecified, so mark them as UTC again.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        modelBuilder.Entity<TransportOrder>(order =>
        {
            order.ToTable("TransportOrders");
            order.HasKey(o => o.Id);
            order.Property(o => o.OrderNumber).IsRequired().HasMaxLength(12);
            order.HasIndex(o => o.OrderNumber).IsUnique();
            order.Property(o => o.Material).IsRequired().HasMaxLength(100);
            order.Property(o => o.SourceLocation).IsRequired().HasMaxLength(100);
            order.Property(o => o.TargetLocation).IsRequired().HasMaxLength(100);
            order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(o => o.CreatedAt).HasConversion(utcConverter);
            order.Property(o => o.UpdatedAt).HasConversion(utcConverter);
        });
    }
}
