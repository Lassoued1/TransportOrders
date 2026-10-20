using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class SchedulingTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly ServiceProvider _services;
    private IOrderRepository Repository => _services.GetRequiredService<IOrderRepository>();
    public SchedulingTests() => _services = new ServiceCollection().AddTransportOrdersData(_database.Path).BuildServiceProvider();
    private Task InitializeAsync() => _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }
    [Fact]
    public async Task Recreated_simulator_executes_the_previewed_action_across_the_complete_flow()
    {
        await InitializeAsync();
        var clock = new FixedClock(DateTime.UtcNow.AddDays(1));
        var seen = new HashSet<ScheduledActionKind>();
        for (var i = 0; i < 300; i++)
        {
            var state = await Repository.GetPlantStateAsync();
            var simulator = new ExtrusionSimulator(Repository, clock);
            var expected = simulator.GetSchedule(state).Next;
            var result = await simulator.StepAsync();
            if (expected is null) { Assert.Equal(SimulationAction.Waiting, result.Action); break; }
            seen.Add(expected.Kind);
            var actual = result.Action switch
            {
                SimulationAction.Arrived => ScheduledActionKind.ArriveTransport,
                SimulationAction.ReturnArrived => ScheduledActionKind.ArriveReturn,
                SimulationAction.PackageShipped => ScheduledActionKind.ArrivePackage,
                SimulationAction.PackingCompleted => ScheduledActionKind.CompletePacking,
                SimulationAction.PackageChecked => ScheduledActionKind.InspectPackage,
                SimulationAction.PackingStarted => ScheduledActionKind.StartPacking,
                SimulationAction.ReturnStarted => ScheduledActionKind.ReturnEmpty,
                SimulationAction.PackageDispatched => ScheduledActionKind.ShipPackage,
                SimulationAction.ProcessCompleted => ScheduledActionKind.CreatePackingOrder,
                SimulationAction.Dispatched => ScheduledActionKind.DispatchBasket,
                SimulationAction.CycleStarted => ScheduledActionKind.StartOvenCycle,
                SimulationAction.CycleAdvanced or SimulationAction.CycleCompleted => ScheduledActionKind.AdvanceOvenCycle,
                _ => throw new InvalidOperationException($"Unexpected action {result.Action}"),
            };
            Assert.Equal(expected.Kind, actual);
            var after = await Repository.GetPlantStateAsync();
            Assert.All(after.Locations, l => Assert.True(after.Occupied(l.Id) + after.Reserved(l.Id) <= l.Capacity));
        }
        Assert.Equal(Enum.GetValues<ScheduledActionKind>().Length, seen.Count);
        Assert.Equal(10, (await Repository.GetPlantStateAsync()).Packages.Count(p => p.Status == PackageStatus.Shipped));
    }
    [Fact]
    public async Task Plan_is_rebuilt_identically_after_restart_without_writing_orders_or_reservations()
    {
        await InitializeAsync();
        var clock = new FixedClock(DateTime.UtcNow.AddHours(1));
        var before = await Repository.GetPlantStateAsync();
        var first = new ExtrusionSimulator(Repository, clock).GetSchedule(before);
        await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var after = await Repository.GetPlantStateAsync();
        var second = new ExtrusionSimulator(Repository, clock).GetSchedule(after);
        Assert.Equal(first.Actions.Select(a => (a.Kind, a.Reference, a.Reason)), second.Actions.Select(a => (a.Kind, a.Reference, a.Reason)));
        Assert.Equal(before.Baskets.Select(b => (b.Number, b.Version, b.ReservedLocationId)), after.Baskets.Select(b => (b.Number, b.Version, b.ReservedLocationId)));
        Assert.Equal(before.Orders.Select(o => o.History.Count), after.Orders.Select(o => o.History.Count));
    }
    [Fact]
    public async Task Replanning_removes_a_candidate_after_quality_hold_and_reports_the_reason()
    {
        await InitializeAsync();
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        var order = (await Repository.GetByOrderNumberAsync("TO-2026-0007"))!;
        var before = ProductionScheduler.Plan(await Repository.GetPlantStateAsync(), DateTime.UtcNow);
        Assert.Contains(before.Actions, a => a.Reference == order.PieceNumber && a.Kind == ScheduledActionKind.DispatchBasket);
        order.TransitionTo(OrderStatus.OnHold, "QA", "Inspection");
        await Repository.SaveAsync(order);
        var after = ProductionScheduler.Plan(await Repository.GetPlantStateAsync(), DateTime.UtcNow);
        Assert.DoesNotContain(after.Actions, a => a.Reference == order.PieceNumber);
        Assert.Contains(after.Blocked, b => b.Reference == order.PieceNumber && b.Reason == ScheduleReason.QualityHold);
    }
    public void Dispose() { _services.Dispose(); _database.Dispose(); }
}
