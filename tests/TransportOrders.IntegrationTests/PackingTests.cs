using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class PackingTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly ServiceProvider _services;
    private IOrderRepository Repository => _services.GetRequiredService<IOrderRepository>();
    private DatabaseInitializer Initializer => _services.GetRequiredService<DatabaseInitializer>();
    public PackingTests() => _services = new ServiceCollection().AddTransportOrdersData(_database.Path).BuildServiceProvider();
    private async Task<PackedPackage> AwaitingAsync()
    {
        await Initializer.InitializeAsync();
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        var basket = (await Repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == "AL-26052-K01");
        await Repository.StartPackingAsync(basket.Number, basket.Version, "test", DateTime.UtcNow);
        var package = Assert.Single((await Repository.GetPlantStateAsync()).Packages);
        await Repository.CompletePackingAsync(package.Number, package.Version, "test", DateTime.UtcNow);
        return Assert.Single((await Repository.GetPlantStateAsync()).Packages);
    }
    private async Task<PackedPackage> ReadyAsync()
    {
        var package = await AwaitingAsync();
        await Repository.InspectPackageAsync(package.Number, package.Version, true, true, "QA", "Checks passed", DateTime.UtcNow);
        return Assert.Single((await Repository.GetPlantStateAsync()).Packages);
    }
    private async Task<Basket> ReturnAsync(PackedPackage package)
    {
        var basket = (await Repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == package.SourceBasketNumber);
        await Repository.StartEmptyReturnAsync(basket.Number, basket.Version, "PRESS-01", "test", DateTime.UtcNow);
        var movement = Assert.Single((await Repository.GetPlantStateAsync()).Returns);
        await Repository.CompleteEmptyReturnAsync(movement.Id, movement.Version, "test", DateTime.UtcNow);
        return (await Repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == basket.Number);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Failed_check_keeps_material_in_basket_and_blocks_label_and_shipping(bool visual, bool quantity)
    {
        var package = await AwaitingAsync();
        await Repository.InspectPackageAsync(package.Number, package.Version, visual, quantity, "QA", "Check failed", DateTime.UtcNow);
        await Initializer.InitializeAsync();
        var state = await Repository.GetPlantStateAsync();
        package = Assert.Single(state.Packages);
        Assert.Equal(PackageStatus.Rejected, package.Status);
        Assert.Null(package.LabelText);
        Assert.False(state.Baskets.Single(b => b.Number == package.SourceBasketNumber).IsEmpty);
        Assert.Equal(EquipmentStatus.Busy, state.Equipment.Single(e => e.Id == "PACK-02").Status);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.StartPackageShippingAsync(package.Number, package.Version, "test", DateTime.UtcNow));
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        Assert.Equal(PackageStatus.Rejected, (await Repository.GetPlantStateAsync()).Packages.Single(p => p.Number == package.Number).Status);
    }

    [Fact]
    public async Task Approval_atomically_labels_unloads_and_preserves_replaced_legacy_order()
    {
        var package = await ReadyAsync();
        var state = await Repository.GetPlantStateAsync();
        var basket = state.Baskets.Single(b => b.Number == package.SourceBasketNumber);
        Assert.True(basket.IsEmpty);
        Assert.Null(basket.CurrentMaterialLotNumber);
        Assert.Equal(0, basket.Quantity);
        Assert.Equal("PACK-02", basket.LocationId);
        Assert.Contains(package.Number, package.LabelText!);
        Assert.Contains(package.MaterialLotNumber, package.LabelText!);
        Assert.Contains($"QUANTITY {package.Quantity}", package.LabelText!);
        Assert.Contains(basket.History, h => h.Action == "Unloaded" && h.Reference == package.Number);
        var old = state.Orders.Single(o => o.OrderNumber == "TO-2026-0009");
        Assert.Equal(OrderStatus.Cancelled, old.Status);
        Assert.Contains(package.Number, old.History[^1].Note!);
        Assert.Equal(EquipmentStatus.Available, state.Equipment.Single(e => e.Id == "PACK-02").Status);
    }

    [Fact]
    public async Task Stale_approval_cannot_override_rejection_or_unload_material()
    {
        var package = await AwaitingAsync();
        await Repository.InspectPackageAsync(package.Number, package.Version, false, true, "QA", "Damage", DateTime.UtcNow);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.InspectPackageAsync(package.Number, package.Version, true, true, "test", "Stale approval", DateTime.UtcNow));
        var state = await Repository.GetPlantStateAsync();
        Assert.False(state.Baskets.Single(b => b.Number == package.SourceBasketNumber).IsEmpty);
        Assert.Equal(PackageStatus.Rejected, Assert.Single(state.Packages).Status);
    }

    [Fact]
    public async Task Explicit_reinspection_can_release_rejected_package()
    {
        var package = await AwaitingAsync();
        await Repository.InspectPackageAsync(package.Number, package.Version, false, true, "QA", "Damage", DateTime.UtcNow);
        package = Assert.Single((await Repository.GetPlantStateAsync()).Packages);
        await Repository.InspectPackageAsync(package.Number, package.Version, true, true, "QA", "Repacked and checked", DateTime.UtcNow);
        package = Assert.Single((await Repository.GetPlantStateAsync()).Packages);
        Assert.Equal(PackageStatus.Ready, package.Status);
        Assert.Contains(package.History, h => h.Status == PackageStatus.Rejected);
    }

    [Fact]
    public async Task Empty_return_reservation_survives_fault_and_restart_without_material_orders()
    {
        var package = await ReadyAsync();
        var basket = (await Repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == package.SourceBasketNumber);
        await Repository.StartEmptyReturnAsync(basket.Number, basket.Version, "PRESS-01", "test", DateTime.UtcNow);
        var state = await Repository.GetPlantStateAsync();
        var movement = Assert.Single(state.Returns);
        var shuttle = state.Equipment.Single(e => e.Id == PlantEquipment.SharedShuttle);
        await Repository.SetEquipmentControlAsync(shuttle.Id, shuttle.Version, EquipmentMode.Automatic, EquipmentCondition.Fault, "test", "Fault", DateTime.UtcNow);
        await Initializer.InitializeAsync();
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.CompleteEmptyReturnAsync(movement.Id, movement.Version, "test", DateTime.UtcNow));
        state = await Repository.GetPlantStateAsync();
        basket = state.Baskets.Single(b => b.Number == basket.Number);
        Assert.Null(basket.LocationId);
        Assert.Equal("PRESS-01", basket.ReservedLocationId);
        Assert.Equal(movement.Id, basket.ActiveReturnId);
        Assert.Equal(12, state.Orders.Count);
        shuttle = state.Equipment.Single(e => e.Id == PlantEquipment.SharedShuttle);
        await Repository.SetEquipmentControlAsync(shuttle.Id, shuttle.Version, EquipmentMode.Automatic, EquipmentCondition.Operational, "test", "Recovery", DateTime.UtcNow);
        await Repository.CompleteEmptyReturnAsync(movement.Id, movement.Version, "test", DateTime.UtcNow);
        basket = (await Repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == basket.Number);
        Assert.Equal("PRESS-01", basket.LocationId);
        Assert.Null(basket.ActiveReturnId);
        Assert.True(basket.IsEmpty);
    }

    [Fact]
    public async Task Ready_package_keeps_packing_slot_after_empty_basket_leaves()
    {
        var package = await ReadyAsync();
        await ReturnAsync(package);
        var state = await Repository.GetPlantStateAsync();
        Assert.Equal(1, state.Occupied("PACK-02"));
        Assert.False(state.HasRoom("PACK-02", state.Locate()));
        await Repository.StartPackageShippingAsync(package.Number, package.Version, "test", DateTime.UtcNow);
        state = await Repository.GetPlantStateAsync();
        Assert.Equal(0, state.Occupied("PACK-02"));
        Assert.Equal(1, state.Reserved(ExtrusionPlant.Shipping));
        package = Assert.Single(state.Packages);
        await Repository.CompletePackageShippingAsync(package.Number, package.Version, "test", DateTime.UtcNow);
        state = await Repository.GetPlantStateAsync();
        Assert.Equal(1, state.Occupied(ExtrusionPlant.Shipping));
        Assert.Equal(0, state.Reserved(ExtrusionPlant.Shipping));
    }

    [Fact]
    public async Task Reload_with_different_batch_preserves_package_snapshot_and_creates_independent_order()
    {
        var package = await ReadyAsync();
        var basket = await ReturnAsync(package);
        var label = package.LabelText;
        await Repository.ReloadBasketAsync(basket.Number, basket.Version, "AL-26031-B01", 17, 1.2m, 6.4m, "OVEN-01", "test", DateTime.UtcNow);
        var state = await Repository.GetPlantStateAsync();
        basket = state.Baskets.Single(b => b.Number == basket.Number);
        Assert.Equal(2, basket.ContentSequence);
        Assert.False(basket.IsEmpty);
        Assert.Equal("AL-26031-B01", basket.CurrentMaterialLotNumber);
        var order = state.Orders.Single(o => o.PieceNumber == basket.Number && o.IsOpen);
        Assert.Equal("AL-26031", order.HeatNumber);
        Assert.Equal(2, order.BasketContentSequence);
        Assert.Equal(17, order.Quantity);
        Assert.Equal(label, Assert.Single(state.Packages).LabelText);
        Assert.NotEqual(package.MaterialLotNumber, basket.MaterialLotNumber);
        Assert.Equal(1, Assert.Single(state.Packages).ContentSequence);
    }

    [Fact]
    public async Task Reusing_same_lot_still_requires_new_treatment_and_produces_distinct_package()
    {
        await Initializer.InitializeAsync();
        var simulator = new ExtrusionSimulator(Repository, TimeProvider.System);
        for (var i = 0; i < 300; i++) if ((await simulator.StepAsync()).Action == SimulationAction.Waiting) break;
        var state = await Repository.GetPlantStateAsync();
        var basket = state.Baskets.Single(b => b.Number == "AL-26031-K01");
        var original = state.Packages.Single(p => p.SourceBasketNumber == basket.Number);
        var originalLabel = original.LabelText;
        await Repository.ReloadBasketAsync(basket.Number, basket.Version, original.MaterialLotNumber, 17, 1.2m, 6.4m, "OVEN-01", "test", DateTime.UtcNow);
        state = await Repository.GetPlantStateAsync();
        basket = state.Baskets.Single(b => b.Number == basket.Number);
        Assert.False(state.HasCompletedTreatment(basket, "OVEN-01"));
        for (var i = 0; i < 100; i++) if ((await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync()).Action == SimulationAction.Waiting) break;
        state = await Repository.GetPlantStateAsync();
        Assert.Equal(11, state.Packages.Count(p => p.Status == PackageStatus.Shipped));
        var packages = state.Packages.Where(p => p.SourceBasketNumber == basket.Number).OrderBy(p => p.ContentSequence).ToList();
        Assert.Equal([1, 2], packages.Select(p => p.ContentSequence));
        Assert.NotEqual(packages[0].Number, packages[1].Number);
        Assert.Equal(17, packages[1].Quantity);
        Assert.Equal(originalLabel, packages[0].LabelText);
        Assert.Contains(state.Cycles, c => c.Load.Any(l => l.BasketNumber == basket.Number && l.ContentSequence == 2));
    }

    [Fact]
    public async Task New_migration_preserves_existing_cycle_and_basket_versions()
    {
        await Initializer.InitializeAsync();
        await Repository.StartOvenCycleAsync("OVEN-01", "DEMO-6060", ["AL-26031-K01", "AL-26031-K02"], "test", DateTime.UtcNow);
        var before = await Repository.GetPlantStateAsync();
        await using (var context = await _services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await context.GetService<IMigrator>().MigrateAsync("20261012050709_EquipmentAndOvenCycles");
        await Initializer.InitializeAsync();
        var after = await Repository.GetPlantStateAsync();
        Assert.Equal(before.Baskets.Select(b => (b.Number, b.Version)), after.Baskets.Select(b => (b.Number, b.Version)));
        Assert.Equal(Assert.Single(before.Cycles).Id, Assert.Single(after.Cycles).Id);
        Assert.All(Assert.Single(after.Cycles).Load, l => Assert.Equal(1, l.ContentSequence));
        Assert.Empty(after.Packages);
        Assert.Empty(after.Returns);
    }

    public void Dispose() { _services.Dispose(); _database.Dispose(); }
}
