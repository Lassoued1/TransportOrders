using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class PlantPersistenceTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly ServiceProvider _services;
    private IOrderRepository Repository => _services.GetRequiredService<IOrderRepository>();
    private DatabaseInitializer Initializer => _services.GetRequiredService<DatabaseInitializer>();
    private IDbContextFactory<AppDbContext> Factory => _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    public PlantPersistenceTests() => _services = new ServiceCollection().AddTransportOrdersData(_database.Path).BuildServiceProvider();

    [Fact]
    public async Task Equipment_migration_preserves_existing_physical_positions_and_quality_history()
    {
        await Initializer.InitializeAsync();
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        var before = await Repository.GetPlantStateAsync();
        await using (var context = await Factory.CreateDbContextAsync())
            await context.GetService<IMigrator>().MigrateAsync("20261012044903_PersistedPlantModel");
        await Initializer.InitializeAsync();
        var after = await Repository.GetPlantStateAsync();
        Assert.Equal(before.Baskets.Select(b => (b.Number, b.LocationId, b.Version)), after.Baskets.Select(b => (b.Number, b.LocationId, b.Version)));
        Assert.Equal(before.Orders.Select(o => (o.Id, o.Status, o.History.Count)), after.Orders.Select(o => (o.Id, o.Status, o.History.Count)));
        Assert.Equal(7, after.Equipment.Count);
        Assert.Equal(3, after.Recipes.Count);
        Assert.Empty(after.Cycles);
    }

    [Fact]
    public async Task Previous_schema_upgrades_without_losing_orders_history_or_active_reservations()
    {
        var now = DateTime.UtcNow;
        int orderId;
        int[] historyIds;
        await using (var context = await Factory.CreateDbContextAsync())
        {
            await context.Database.MigrateAsync();
            var order = new TransportOrder("TO-2026-0042", "AL-42", "AL-42-K01", "AL-42-B01",
                "EN AW-6060 profiles", 10, 1m, 6.4m, "PRESS-01", "OVEN-01", OrderPriority.Normal,
                now.AddHours(1), now, "test");
            var transport = Transport.Dispatch("TR-2026-0042", ExtrusionSimulator.Shuttle, [order], "test", now);
            context.Transports.Add(transport);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            orderId = order.Id;
            historyIds = order.History.Select(h => h.Id).ToArray();
            await context.GetService<IMigrator>().MigrateAsync("20260929052810_SteelPlantTransports");
        }
        Assert.False((await Initializer.InitializeAsync()).Seeded);
        var state = await Repository.GetPlantStateAsync();
        var stored = Assert.Single(state.Orders);
        Assert.Equal(orderId, stored.Id);
        Assert.Equal(historyIds, stored.History.Select(h => h.Id));
        var basket = Assert.Single(state.Baskets);
        Assert.Null(basket.LocationId);
        Assert.Equal("OVEN-01", basket.ReservedLocationId);
        Assert.Equal("TR-2026-0042", basket.ActiveTransportNumber);
        var version = basket.Version;
        await Initializer.InitializeAsync();
        Assert.Equal(version, Assert.Single((await Repository.GetPlantStateAsync()).Baskets).Version);
    }

    [Fact]
    public async Task Registered_basket_without_an_order_is_visible_and_survives_startup()
    {
        await Initializer.InitializeAsync();
        await using (var context = await Factory.CreateDbContextAsync())
        {
            context.MaterialLots.Add(new("AL-NEW-B01", "AL-NEW", "EN AW-6063 profiles"));
            context.Baskets.Add(new("AL-NEW-K01", "AL-NEW-B01", 10, 1m, 6m, "PRESS-01"));
            await context.SaveChangesAsync();
        }
        await Initializer.InitializeAsync();
        var basket = (await Repository.GetPlantStateAsync()).Locate().Single(b => b.BasketNumber == "AL-NEW-K01");
        Assert.Null(basket.Order);
        Assert.Equal("PRESS-01", basket.Location);
        Assert.Equal("AL-NEW", basket.MaterialLot.BatchNumber);
    }

    [Fact]
    public async Task Arrival_atomically_releases_reservation_and_persists_position_and_history()
    {
        await Initializer.InitializeAsync();
        var before = (await Repository.GetPlantStateAsync()).Baskets.Single(b => b.IsMoving);
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        await Initializer.InitializeAsync();
        var state = await Repository.GetPlantStateAsync();
        var after = state.Baskets.Single(b => b.Number == before.Number);
        Assert.Equal(before.ReservedLocationId, after.LocationId);
        Assert.Null(after.ReservedLocationId);
        Assert.Null(after.ActiveTransportNumber);
        Assert.NotEqual(before.Version, after.Version);
        Assert.Equal(TransportStatus.Done, state.Transports.Single(t => t.TransportNumber == before.ActiveTransportNumber).Status);
        Assert.All(state.Orders.Where(o => o.TransportNumber == before.ActiveTransportNumber), o =>
        {
            Assert.Equal(OrderStatus.Done, o.Status);
            Assert.Equal(OrderStatus.Done, o.History[^1].ToStatus);
        });
    }

    [Fact]
    public async Task Stale_dispatch_cannot_overwrite_a_new_quality_hold()
    {
        await Initializer.InitializeAsync();
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        var stale = (await Repository.GetByOrderNumberAsync("TO-2026-0007"))!;
        var fresh = (await Repository.GetByOrderNumberAsync(stale.OrderNumber))!;
        fresh.TransitionTo(OrderStatus.OnHold, "QA", "Inspection required");
        await Repository.SaveAsync(fresh);
        var transport = Transport.Dispatch("TR-2026-0099", ExtrusionSimulator.Shuttle, [stale], "test", DateTime.UtcNow);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.SaveTransportAsync(transport, [stale]));
        var state = await Repository.GetPlantStateAsync();
        Assert.Equal(OrderStatus.OnHold, state.Orders.Single(o => o.Id == fresh.Id).Status);
        Assert.Equal("PRESS-02", state.Baskets.Single(b => b.Number == fresh.PieceNumber).LocationId);
        Assert.DoesNotContain(state.Baskets, b => b.IsMoving);
        Assert.DoesNotContain(state.Transports, t => t.TransportNumber == transport.TransportNumber);
    }

    [Fact]
    public async Task Full_destination_rejects_dispatch_without_partial_writes()
    {
        await Initializer.InitializeAsync();
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        var order = (await Repository.GetByOrderNumberAsync("TO-2026-0010"))!;
        var before = (await Repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == order.PieceNumber);
        var transport = Transport.Dispatch("TR-2026-0099", ExtrusionSimulator.Shuttle, [order], "test", DateTime.UtcNow);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.SaveTransportAsync(transport, [order]));
        var state = await Repository.GetPlantStateAsync();
        Assert.Equal(before.Version, state.Baskets.Single(b => b.Number == order.PieceNumber).Version);
        Assert.Equal(OrderStatus.Planned, state.Orders.Single(o => o.Id == order.Id).Status);
        Assert.DoesNotContain(state.Transports, t => t.TransportNumber == transport.TransportNumber);
    }

    [Fact]
    public async Task New_order_cannot_relocate_basket_or_duplicate_an_open_leg()
    {
        await Initializer.InitializeAsync();
        var original = (await Repository.GetByOrderNumberAsync("TO-2026-0003"))!;
        TransportOrder Next(string source) => new("TO-2026-0099", original.HeatNumber, original.PieceNumber,
            original.ParentPieceNumber, original.Material, original.Quantity, original.Weight, original.LoadingLength,
            source, "PACK-01", OrderPriority.Normal, DateTime.UtcNow.AddHours(1), DateTime.UtcNow, "test");
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.SaveAsync(Next("OVEN-01")));
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.SaveAsync(Next(original.SourceLocation)));
        var state = await Repository.GetPlantStateAsync();
        Assert.Equal("PRESS-01", state.Baskets.Single(b => b.Number == original.PieceNumber).LocationId);
        Assert.Equal(12, state.Orders.Count);
    }

    public void Dispose() { _services.Dispose(); _database.Dispose(); }
}
