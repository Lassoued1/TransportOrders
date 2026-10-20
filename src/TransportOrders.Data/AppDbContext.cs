using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TransportOrder> Orders => Set<TransportOrder>();
    public DbSet<PlantAlarm> Alarms => Set<PlantAlarm>();

    public DbSet<Transport> Transports => Set<Transport>();
    public DbSet<Basket> Baskets => Set<Basket>();
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<PlantLocation> Locations => Set<PlantLocation>();
    public DbSet<PlantEquipment> Equipment => Set<PlantEquipment>();
    public DbSet<OvenRecipe> Recipes => Set<OvenRecipe>();
    public DbSet<OvenCycle> OvenCycles => Set<OvenCycle>();
    public DbSet<PackedPackage> Packages => Set<PackedPackage>();
    public DbSet<BasketReturn> BasketReturns => Set<BasketReturn>();
    public DbSet<PlcCommand> PlcCommands => Set<PlcCommand>();
    public DbSet<PlcInboxEntry> PlcInbox => Set<PlcInboxEntry>();
    public DbSet<PlcSimulatorExecution> PlcExecutions => Set<PlcSimulatorExecution>();
    public DbSet<PlcSimulationSettings> PlcSimulation => Set<PlcSimulationSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlantAlarm>(alarm =>
        {
            alarm.ToTable("PlantAlarms"); alarm.HasKey(a => a.Id);
            alarm.Property(a => a.Version).IsConcurrencyToken();
            alarm.Property(a => a.Code).HasConversion<string>();
            alarm.Property(a => a.RaisedUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            alarm.Property(a => a.ClearedUtc).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
            alarm.Property(a => a.AcknowledgedUtc).HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
            alarm.HasIndex(a => a.Key).IsUnique().HasFilter("IsActive = 1");
            alarm.HasMany(a => a.History).WithOne().HasForeignKey("AlarmId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            alarm.Navigation(a => a.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<AlarmEvent>(entry => { entry.ToTable("AlarmEvents"); entry.HasKey(e => e.Id);
            entry.Property(e => e.AtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)); });
        modelBuilder.Entity<MaterialLot>(lot =>
        {
            lot.ToTable("MaterialLots");
            lot.HasKey(l => l.Number);
            lot.Property(l => l.Number).HasMaxLength(30);
            lot.Property(l => l.BatchNumber).IsRequired().HasMaxLength(30);
            lot.Property(l => l.Material).IsRequired().HasMaxLength(100);
        });
        modelBuilder.Entity<PlantLocation>(location =>
        {
            location.ToTable("PlantLocations", table => table.HasCheckConstraint("CK_PlantLocations_Capacity", "Capacity > 0"));
            location.HasKey(l => l.Id);
            location.Property(l => l.Id).HasMaxLength(100);
            location.Property(l => l.Kind).HasConversion<string>().HasMaxLength(20);
        });
        modelBuilder.Entity<Basket>(basket =>
        {
            basket.ToTable("Baskets", table => table.HasCheckConstraint("CK_Baskets_Position",
                "(LocationId IS NOT NULL AND ReservedLocationId IS NULL AND ActiveTransportNumber IS NULL AND ActiveReturnId IS NULL) OR " +
                "(LocationId IS NULL AND ReservedLocationId IS NOT NULL AND ((ActiveTransportNumber IS NOT NULL AND ActiveReturnId IS NULL) OR (ActiveTransportNumber IS NULL AND ActiveReturnId IS NOT NULL)))"));
            basket.HasKey(b => b.Number);
            basket.Property(b => b.Number).HasMaxLength(30);
            basket.Property(b => b.Version).IsConcurrencyToken();
            basket.Ignore(b => b.IsMoving);
            basket.Ignore(b => b.IsEmpty); basket.Ignore(b => b.CurrentMaterialLotNumber);
            basket.Property(b => b.ContentSequence).HasDefaultValue(1);
            basket.HasOne<BasketReturn>().WithMany().HasForeignKey(b => b.ActiveReturnId).OnDelete(DeleteBehavior.Restrict);
            basket.HasMany(b => b.History).WithOne().HasForeignKey("BasketNumber").IsRequired().OnDelete(DeleteBehavior.Cascade);
            basket.Navigation(b => b.History).UsePropertyAccessMode(PropertyAccessMode.Field);
            basket.HasOne<MaterialLot>().WithMany().HasForeignKey(b => b.MaterialLotNumber).OnDelete(DeleteBehavior.Restrict);
            basket.HasOne<PlantLocation>().WithMany().HasForeignKey(b => b.LocationId).OnDelete(DeleteBehavior.Restrict);
            basket.HasOne<PlantLocation>().WithMany().HasForeignKey(b => b.ReservedLocationId).OnDelete(DeleteBehavior.Restrict);
            basket.HasOne<Transport>().WithMany().HasForeignKey(b => b.ActiveTransportNumber)
                .HasPrincipalKey(t => t.TransportNumber).OnDelete(DeleteBehavior.Restrict);
        });
        // SQLite has no date type: values come back with DateTimeKind.Unspecified, so mark them as UTC again.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

        modelBuilder.Entity<PlcCommand>(command =>
        {
            command.ToTable("PlcCommands"); command.HasKey(c => c.Id);
            command.HasIndex(c => new { c.Kind, c.Operation }).IsUnique();
            command.Property(c => c.Kind).HasConversion<string>(); command.Property(c => c.Status).HasConversion<string>();
            command.Property(c => c.Version).IsConcurrencyToken();
            command.Property(c => c.CreatedAt).HasConversion(utcConverter);
            command.Property(c => c.LastAttemptAt).HasConversion(nullableUtcConverter);
            command.Property(c => c.DeadlineAt).HasConversion(nullableUtcConverter);
            command.Property(c => c.AppliedAt).HasConversion(nullableUtcConverter);
            command.HasMany(c => c.History).WithOne().HasForeignKey("CommandId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            command.Navigation(c => c.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<PlcEvent>(entry => { entry.ToTable("PlcEvents"); entry.HasKey(e => e.Id); entry.Property(e => e.At).HasConversion(utcConverter); });
        modelBuilder.Entity<PlcInboxEntry>(entry =>
        {
            entry.ToTable("PlcInbox"); entry.HasKey(e => e.Id); entry.Property(e => e.ReceivedAt).HasConversion(utcConverter);
            entry.Property(e => e.Result).HasConversion<string>();
            entry.HasOne<PlcCommand>().WithMany().HasForeignKey(e => e.CommandId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PlcSimulatorExecution>(execution =>
        {
            execution.ToTable("PlcSimulatorExecutions"); execution.HasKey(e => e.CommandId);
            execution.HasOne<PlcCommand>().WithMany().HasForeignKey(e => e.CommandId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PlcSimulationSettings>(settings =>
        {
            settings.ToTable("PlcSimulationSettings"); settings.HasKey(s => s.Id);
            settings.Property(s => s.Version).IsConcurrencyToken(); settings.Property(s => s.ChangedAt).HasConversion(utcConverter);
        });

        modelBuilder.Entity<BasketEvent>(entry =>
        {
            entry.ToTable("BasketEvents"); entry.HasKey(e => e.Id);
            entry.Property(e => e.At).HasConversion(utcConverter);
        });
        modelBuilder.Entity<BasketReturn>(movement =>
        {
            movement.ToTable("BasketReturns"); movement.HasKey(m => m.Id);
            movement.Property(m => m.Version).IsConcurrencyToken();
            movement.Property(m => m.StartedAt).HasConversion(utcConverter);
            movement.Property(m => m.ArrivedAt).HasConversion(nullableUtcConverter);
            movement.HasOne<Basket>().WithMany().HasForeignKey(m => m.BasketNumber).OnDelete(DeleteBehavior.Restrict);
            movement.HasOne<PlantLocation>().WithMany().HasForeignKey(m => m.SourceLocation).OnDelete(DeleteBehavior.Restrict);
            movement.HasOne<PlantLocation>().WithMany().HasForeignKey(m => m.TargetLocation).OnDelete(DeleteBehavior.Restrict);
            movement.HasIndex(m => m.BasketNumber).IsUnique().HasFilter("ArrivedAt IS NULL");
        });
        modelBuilder.Entity<PackedPackage>(package =>
        {
            package.ToTable("PackedPackages"); package.HasKey(p => p.Number);
            package.HasIndex(p => new { p.SourceBasketNumber, p.ContentSequence }).IsUnique();
            package.Property(p => p.Version).IsConcurrencyToken();
            package.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
            package.Property(p => p.CreatedAt).HasConversion(utcConverter);
            package.Property(p => p.ShippedAt).HasConversion(nullableUtcConverter);
            package.HasOne<Basket>().WithMany().HasForeignKey(p => p.SourceBasketNumber).OnDelete(DeleteBehavior.Restrict);
            package.HasOne<MaterialLot>().WithMany().HasForeignKey(p => p.MaterialLotNumber).OnDelete(DeleteBehavior.Restrict);
            package.HasOne<PlantEquipment>().WithMany().HasForeignKey(p => p.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            package.HasOne<PlantLocation>().WithMany().HasForeignKey(p => p.LocationId).OnDelete(DeleteBehavior.Restrict);
            package.HasOne<PlantLocation>().WithMany().HasForeignKey(p => p.ReservedLocationId).OnDelete(DeleteBehavior.Restrict);
            package.HasMany(p => p.History).WithOne().HasForeignKey("PackageNumber").IsRequired().OnDelete(DeleteBehavior.Cascade);
            package.Navigation(p => p.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<PackageEvent>(entry =>
        {
            entry.ToTable("PackageEvents"); entry.HasKey(e => e.Id);
            entry.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
            entry.Property(e => e.At).HasConversion(utcConverter);
        });

        modelBuilder.Entity<PlantEquipment>(equipment =>
        {
            equipment.ToTable("PlantEquipment");
            equipment.HasKey(e => e.Id);
            equipment.Property(e => e.Id).HasMaxLength(100);
            equipment.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
            equipment.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
            equipment.Property(e => e.Condition).HasConversion<string>().HasMaxLength(20);
            equipment.Property(e => e.Version).IsConcurrencyToken();
            equipment.Ignore(e => e.Status); equipment.Ignore(e => e.CanOperate); equipment.Ignore(e => e.CanSimulate);
            equipment.HasOne<PlantLocation>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
            equipment.HasIndex(e => e.LocationId).IsUnique();
            equipment.HasMany(e => e.History).WithOne().HasForeignKey("EquipmentId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            equipment.Navigation(e => e.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<EquipmentEvent>(entry =>
        {
            entry.ToTable("EquipmentEvents"); entry.HasKey(e => e.Id);
            entry.Property(e => e.At).HasConversion(utcConverter);
            entry.Property(e => e.By).IsRequired().HasMaxLength(100);
            entry.Property(e => e.Description).IsRequired().HasMaxLength(300);
        });
        modelBuilder.Entity<OvenRecipe>(recipe =>
        {
            recipe.ToTable("OvenRecipes", t => t.HasCheckConstraint("CK_OvenRecipes_Durations", "HeatingMinutes > 0 AND HoldingMinutes > 0 AND CoolingMinutes > 0"));
            recipe.HasKey(r => r.Id); recipe.Property(r => r.Id).HasMaxLength(100);
            recipe.Property(r => r.Alloy).IsRequired().HasMaxLength(4);
            recipe.Property(r => r.Revision).HasDefaultValue(1);
            recipe.Property(r => r.Version).IsConcurrencyToken();
            recipe.Property(r => r.Status).HasConversion<string>().HasDefaultValue(RecipeStatus.Validated).ValueGeneratedNever();
            recipe.HasMany(r => r.History).WithOne().HasForeignKey("RecipeId").IsRequired().OnDelete(DeleteBehavior.Cascade);
            recipe.Navigation(r => r.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<RecipeEvent>(entry =>
        {
            entry.ToTable("RecipeEvents"); entry.HasKey(e => e.Id);
            entry.Property(e => e.Status).HasConversion<string>();
            entry.Property(e => e.AtUtc).HasConversion(utcConverter);
        });
        modelBuilder.Entity<OvenCycle>(cycle =>
        {
            cycle.ToTable("OvenCycles", t => t.HasCheckConstraint("CK_OvenCycles_Elapsed", "ElapsedMinutes >= 0 AND ElapsedMinutes <= HeatingMinutes + HoldingMinutes + CoolingMinutes"));
            cycle.HasKey(c => c.Id);
            cycle.Property(c => c.RecipeRevision).HasDefaultValue(1);
            cycle.Property(c => c.Version).IsConcurrencyToken();
            cycle.Property(c => c.Phase).HasConversion<string>().HasMaxLength(20);
            cycle.Property(c => c.StartedAt).HasConversion(utcConverter);
            cycle.Property(c => c.UpdatedAt).HasConversion(utcConverter);
            cycle.Property(c => c.CompletedAt).HasConversion(nullableUtcConverter);
            cycle.Ignore(c => c.TotalMinutes); cycle.Ignore(c => c.SimulatedTemperature);
            cycle.HasOne<PlantEquipment>().WithMany().HasForeignKey(c => c.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            cycle.HasOne<OvenRecipe>().WithMany().HasForeignKey(c => c.RecipeId).OnDelete(DeleteBehavior.Restrict);
            cycle.HasIndex(c => c.EquipmentId).IsUnique().HasFilter("Phase <> 'Completed'");
            cycle.HasMany(c => c.Load).WithOne().HasForeignKey(l => l.CycleId).OnDelete(DeleteBehavior.Cascade);
            cycle.Navigation(c => c.Load).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<OvenCycleLoad>(load =>
        {
            load.ToTable("OvenCycleLoads"); load.HasKey(l => new { l.CycleId, l.BasketNumber });
            load.Property(l => l.ContentSequence).HasDefaultValue(1);
            load.HasOne<Basket>().WithMany().HasForeignKey(l => l.BasketNumber).OnDelete(DeleteBehavior.Restrict);
            load.HasOne<MaterialLot>().WithMany().HasForeignKey(l => l.MaterialLotNumber).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransportOrder>(order =>
        {
            order.ToTable("TransportOrders");
            order.HasKey(o => o.Id);
            order.Property(o => o.BasketContentSequence).HasDefaultValue(1);
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
