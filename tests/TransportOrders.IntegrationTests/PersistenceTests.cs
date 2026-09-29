using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class PersistenceTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly ServiceProvider _services;

    public PersistenceTests()
    {
        _services = new ServiceCollection().AddTransportOrdersData(_database.Path).BuildServiceProvider();
    }

    private DatabaseInitializer Initializer => _services.GetRequiredService<DatabaseInitializer>();

    private IOrderRepository Repository => _services.GetRequiredService<IOrderRepository>();

    [Fact]
    public async Task Demo_data_is_seeded_once_with_consistent_history_and_transports()
    {
        var first = await Initializer.InitializeAsync();
        var second = await Initializer.InitializeAsync();

        Assert.True(first.Seeded);
        Assert.False(second.Seeded);
        var orders = await Repository.GetAllAsync();
        Assert.Equal(12, orders.Count);
        Assert.All(orders, o =>
        {
            Assert.Equal(o.Status, o.History[^1].ToStatus);
            Assert.Null(o.History[0].FromStatus);
            Assert.Equal(o.UpdatedAt, o.History[^1].ChangedAt);
            Assert.True(o.Weight > 0 && o.LoadingLength > 0);
        });
        Assert.Equal([7, 1, 1, 2, 1], Enum.GetValues<OrderStatus>().Select(s => orders.Count(o => o.Status == s)));

        var transports = await Repository.GetTransportsAsync();
        Assert.Equal(2, transports.Count);
        Assert.All(transports, t => Assert.Equal(t.OrderCount, orders.Count(o => o.TransportNumber == t.TransportNumber)));
        Assert.Contains(transports, t => t.PartLoadReason == PartLoadReason.PayloadLimit && t.Status == TransportStatus.Done);
        Assert.Contains(transports, t => t.PartLoadReason == PartLoadReason.UrgentMaterial && t.Status == TransportStatus.InTransit);

        // The demo history names the plant's departments.
        Assert.All(orders, o => Assert.Equal(DatabaseInitializer.PlanningUser, o.History[0].ChangedBy));
        Assert.All(transports, t => Assert.Equal(DatabaseInitializer.DispatchUser, t.DispatchedBy));
        Assert.Equal(DatabaseInitializer.QualityUser, orders.Single(o => o.Status == OrderStatus.OnHold).History[^1].ChangedBy);
        Assert.Equal(("Planung", "Disposition", "Qualitätssicherung"),
            (DatabaseInitializer.PlanningUser, DatabaseInitializer.DispatchUser, DatabaseInitializer.QualityUser));
    }

    [Fact]
    public async Task Dispatched_transport_and_its_orders_are_saved_together()
    {
        await Initializer.InitializeAsync();
        var now = DateTime.UtcNow;
        var orders = await Repository.GetAllAsync();
        var plan = LoadPlanner.Plan(orders, Fleet.Default, now);
        var proposal = plan.Proposals.First(p => p.IsFull);
        var number = await Repository.NextTransportNumberAsync(now);

        var transport = Transport.Dispatch(number, proposal.Vehicle, proposal.Orders, "dispatch.test", now);
        await Repository.SaveTransportAsync(transport, proposal.Orders);

        Assert.Equal($"TR-{now.Year}-0003", number);
        var reloaded = (await Repository.GetAllAsync()).Where(o => o.TransportNumber == number).ToList();
        Assert.Equal(proposal.Orders.Count, reloaded.Count);
        Assert.All(reloaded, o =>
        {
            Assert.Equal(OrderStatus.InTransit, o.Status);
            Assert.Equal((number, "dispatch.test"), (o.History[^1].Note, o.History[^1].ChangedBy));
            Assert.Equal(now, o.History[^1].ChangedAt, TimeSpan.FromMilliseconds(1));
        });
        var stored = (await Repository.GetTransportsAsync()).Single(t => t.TransportNumber == number);
        Assert.Equal((proposal.LoadedLength, proposal.Vehicle.LoadingLength, TransportStatus.InTransit),
            (stored.LoadedLength, stored.LoadingLength, stored.Status));
    }

    [Fact]
    public async Task Hold_reason_survives_a_reload()
    {
        await Initializer.InitializeAsync();
        var order = await Repository.GetByOrderNumberAsync("TO-2026-0003");

        order!.TransitionTo(OrderStatus.OnHold, "qa.test", "Surface cracks");
        await Repository.SaveAsync(order);

        var reloaded = await Repository.GetByOrderNumberAsync("to-2026-0003");
        Assert.Equal((OrderStatus.OnHold, "Surface cracks"), (reloaded!.Status, reloaded.HoldReason));
    }

    [Fact]
    public async Task Lookup_by_order_number_ignores_case_and_spaces()
    {
        await Initializer.InitializeAsync();

        Assert.NotNull(await Repository.GetByOrderNumberAsync("  to-2026-0007 "));
        Assert.Null(await Repository.GetByOrderNumberAsync("TO-2026-9999"));
    }

    [Fact]
    public async Task Order_number_is_unique_in_the_database()
    {
        await Initializer.InitializeAsync();
        var factory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;

        context.Orders.Add(new TransportOrder("TO-2026-0001", "H-1000", "H-1000-1", null, "1.2379 ingot", 1, 5m, 2m,
            "Melt shop", "ESR plant", OrderPriority.Normal, now.AddHours(1), now, "test"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_from_version_2_is_migrated_and_gets_the_steel_demo_data()
    {
        // Version 2: schema of the first migration with an aluminium order in it.
        var factory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var context = await factory.CreateDbContextAsync())
        {
            await context.GetService<IMigrator>().MigrateAsync("20260923192054_InitialCreate");
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO TransportOrders (OrderNumber, HeatNumber, Material, Quantity, SourceLocation, TargetLocation, " +
                "Priority, DueAt, Status, CreatedAt, UpdatedAt) VALUES ('TO-2026-0001', 'H-26031', 'Billet 6060', 40, " +
                "'Warehouse A', 'Press 1', 'Normal', '2026-03-02 12:00:00', 'Planned', '2026-03-02 08:00:00', '2026-03-02 08:00:00')");
        }

        var result = await Initializer.InitializeAsync();

        Assert.True(result.Seeded);
        Assert.Null(result.LegacyBackupPath);
        var orders = await Repository.GetAllAsync();
        Assert.Equal(12, orders.Count);
        Assert.DoesNotContain(orders, o => o.Material.StartsWith("Billet"));
    }

    [Fact]
    public async Task Database_from_version_1_is_backed_up_and_replaced()
    {
        // Version 1 used EnsureCreated: the table exists but there is no migrations history.
        await using (var connection = new SqliteConnection($"Data Source={_database.Path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE TransportOrders (Id INTEGER PRIMARY KEY, OrderNumber TEXT NOT NULL)";
            await command.ExecuteNonQueryAsync();
        }

        var result = await Initializer.InitializeAsync();

        Assert.NotNull(result.LegacyBackupPath);
        Assert.True(File.Exists(result.LegacyBackupPath));
        Assert.True(result.Seeded);
        Assert.Equal(12, (await Repository.GetAllAsync()).Count);
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }
}
