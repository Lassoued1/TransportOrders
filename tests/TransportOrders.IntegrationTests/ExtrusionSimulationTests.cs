using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class ExtrusionSimulationTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly ServiceProvider _services;
    private IOrderRepository Repository => _services.GetRequiredService<IOrderRepository>();

    public ExtrusionSimulationTests() =>
        _services = new ServiceCollection().AddTransportOrdersData(_database.Path).BuildServiceProvider();

    private async Task InitializeAsync() =>
        await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();

    [Fact]
    public async Task Baskets_reach_shipping_without_overbooking_or_moving_held_material_even_after_restarts()
    {
        await InitializeAsync();
        SimulationResult result = new(SimulationAction.Waiting);
        for (var step = 0; step < 300; step++)
        {
            // Recreate every time: progress comes from SQLite, not process memory.
            result = await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
            var state = await Repository.GetPlantStateAsync();
            var baskets = state.Locate();
            Assert.Equal(12, baskets.Count);
            Assert.All(state.Locations, s => Assert.True(state.Occupied(s.Id) + state.Reserved(s.Id) <= s.Capacity, s.Id));
            Assert.True(baskets.Count(b => b.IsMoving) + state.Packages.Count(p => p.Status == PackageStatus.InTransit) <= 1);
            if (result.Action == SimulationAction.Waiting) break;
        }
        Assert.Equal(SimulationAction.Waiting, result.Action);
        var orders = await Repository.GetAllAsync();
        var final = (await Repository.GetPlantStateAsync()).Locate();
        var completed = await Repository.GetPlantStateAsync();
        Assert.Equal(10, completed.Packages.Count(p => p.Status == PackageStatus.Shipped));
        Assert.Equal(10, completed.Baskets.Count(b => b.IsEmpty));
        Assert.All(completed.Baskets.Where(b => b.IsEmpty), b => Assert.StartsWith("PRESS-", b.LocationId));
        Assert.All(completed.Packages, p => Assert.False(string.IsNullOrWhiteSpace(p.LabelText)));
        Assert.All(completed.PlcCommands, c => Assert.Equal(PlcCommandStatus.Applied, c.Status));
        Assert.Contains(completed.PlcCommands, c => c.Kind == PlcCommandKind.EmptyReturn);
        Assert.Contains(completed.PlcCommands, c => c.Kind == PlcCommandKind.PackageTransfer);
        Assert.Equal(OrderStatus.OnHold, final.Single(b => b.BasketNumber == "AL-26052-K03").Order!.Status);
        Assert.Equal(OrderStatus.Cancelled, final.Single(b => b.BasketNumber == "AL-26058-K01").Order!.Status);
        var route = orders.Where(o => o.PieceNumber == "AL-26031-K03").OrderBy(o => o.CreatedAt).ToList();
        Assert.Equal(2, route.Count);
        Assert.Equal("PRESS-01", route[0].SourceLocation);
        Assert.Equal("OVEN-02", route[0].TargetLocation);
        Assert.StartsWith("PACK-", route[1].TargetLocation);
        Assert.Equal("SHIP-01", completed.Packages.Single(p => p.SourceBasketNumber == "AL-26031-K03").LocationId);
        Assert.All(route, o => Assert.Equal(OrderStatus.Done, o.Status));
        Assert.All(route, o => Assert.Equal(o.Status, o.History[^1].ToStatus));
    }

    [Fact]
    public async Task Fault_freezes_a_transfer_and_keeps_its_destination_reserved_until_recovery()
    {
        await InitializeAsync();
        var simulator = new ExtrusionSimulator(Repository, TimeProvider.System);
        var shuttle = (await Repository.GetPlantStateAsync()).Equipment.Single(e => e.Id == PlantEquipment.SharedShuttle);
        await simulator.SetEquipmentControlAsync(shuttle.Id, shuttle.Version, EquipmentMode.Automatic, EquipmentCondition.Fault, "test", "Injected fault");
        Assert.Equal(SimulationAction.Faulted, (await simulator.StepAsync()).Action);
        var baskets = (await Repository.GetPlantStateAsync()).Locate();
        Assert.Equal(1, ExtrusionPlant.Reserved("PACK-01", baskets));
        Assert.Equal(0, ExtrusionPlant.Occupied("PACK-01", baskets));
        shuttle = (await Repository.GetPlantStateAsync()).Equipment.Single(e => e.Id == PlantEquipment.SharedShuttle);
        await simulator.SetEquipmentControlAsync(shuttle.Id, shuttle.Version, EquipmentMode.Automatic, EquipmentCondition.Operational, "test", "Recovery");
        Assert.Equal(SimulationAction.Arrived, (await simulator.StepAsync()).Action);
        baskets = (await Repository.GetPlantStateAsync()).Locate();
        Assert.Equal(0, ExtrusionPlant.Reserved("PACK-01", baskets));
        Assert.Equal(1, ExtrusionPlant.Occupied("PACK-01", baskets));
    }

    [Fact]
    public async Task Released_quality_hold_can_resume_the_complete_route()
    {
        await InitializeAsync();
        var order = (await Repository.GetAllAsync()).Single(o => o.Status == OrderStatus.OnHold);
        order.TransitionTo(OrderStatus.Planned, "QA", DateTime.UtcNow);
        await Repository.SaveAsync(order);
        var simulator = new ExtrusionSimulator(Repository, TimeProvider.System);
        for (var i = 0; i < 300; i++)
            if ((await simulator.StepAsync()).Action == SimulationAction.Waiting) break;
        Assert.Equal(11, (await Repository.GetPlantStateAsync()).Packages.Count(p => p.Status == PackageStatus.Shipped));
    }

    public void Dispose() { _services.Dispose(); _database.Dispose(); }
}
