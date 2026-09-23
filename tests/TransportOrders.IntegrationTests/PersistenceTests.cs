using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    public async Task Demo_orders_are_seeded_once_with_consistent_history()
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
        });
        Assert.Equal([4, 3, 3, 2], Enum.GetValues<OrderStatus>().Select(s => orders.Count(o => o.Status == s)));
    }

    [Fact]
    public async Task Status_change_and_its_history_entry_survive_a_reload()
    {
        await Initializer.InitializeAsync();
        var order = await Repository.GetByOrderNumberAsync("TO-2026-0001");
        var changedAt = DateTime.UtcNow;

        order!.TransitionTo(OrderStatus.InTransit, "driver.test", changedAt);
        await Repository.SaveAsync(order);

        var reloaded = await Repository.GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.InTransit, reloaded!.Status);
        Assert.Equal(2, reloaded.History.Count);
        Assert.Equal((OrderStatus.Planned, OrderStatus.InTransit, "driver.test"),
            (reloaded.History[1].FromStatus, reloaded.History[1].ToStatus, reloaded.History[1].ChangedBy));
        Assert.Equal(changedAt, reloaded.History[1].ChangedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(DateTimeKind.Utc, reloaded.UpdatedAt.Kind);
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

        context.Orders.Add(new TransportOrder("TO-2026-0001", "H-1000", "Billet 6060", 1, "Warehouse A", "Press 1",
            OrderPriority.Normal, now.AddHours(1), now, "test"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
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
