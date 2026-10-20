using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class OvenProcessTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly ServiceProvider _services;
    private IOrderRepository Repository => _services.GetRequiredService<IOrderRepository>();
    private DatabaseInitializer Initializer => _services.GetRequiredService<DatabaseInitializer>();
    public OvenProcessTests() => _services = new ServiceCollection().AddTransportOrdersData(_database.Path).BuildServiceProvider();
    private async Task<OvenCycle> StartAsync()
    {
        await Initializer.InitializeAsync();
        var state = await Repository.GetPlantStateAsync();
        await Repository.StartOvenCycleAsync("OVEN-01", "DEMO-6060", state.Baskets.Where(b => b.LocationId == "OVEN-01")
            .Select(b => b.Number).ToArray(), "test", DateTime.UtcNow);
        return Assert.Single((await Repository.GetPlantStateAsync()).Cycles);
    }
    private async Task ControlAsync(string id, EquipmentMode mode, EquipmentCondition condition)
    {
        var equipment = (await Repository.GetPlantStateAsync()).Equipment.Single(e => e.Id == id);
        await Repository.SetEquipmentControlAsync(id, equipment.Version, mode, condition, "test", "Operator change", DateTime.UtcNow);
    }

    [Fact]
    public async Task Recipe_change_does_not_modify_an_existing_cycle_snapshot()
    {
        var cycle = await StartAsync();
        await using (var context = await _services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var recipe = await context.Recipes.SingleAsync(r => r.Id == cycle.RecipeId);
            context.Entry(recipe).Property(r => r.HoldingMinutes).CurrentValue = 9;
            context.Entry(recipe).Property(r => r.TargetTemperature).CurrentValue = 200m;
            await context.SaveChangesAsync();
        }
        await Initializer.InitializeAsync();
        var state = await Repository.GetPlantStateAsync();
        Assert.Equal(9, state.Recipes.Single(r => r.Id == cycle.RecipeId).HoldingMinutes);
        cycle = Assert.Single(state.Cycles);
        Assert.Equal(3, cycle.HoldingMinutes);
        Assert.Equal(180m, cycle.TargetTemperature);
        Assert.Equal(7, cycle.TotalMinutes);
    }

    [Fact]
    public async Task Quality_hold_prevents_oven_start_without_changing_equipment()
    {
        await Initializer.InitializeAsync();
        var original = (await Repository.GetByOrderNumberAsync("TO-2026-0001"))!;
        var outgoing = new TransportOrder("TO-2026-0099", original.HeatNumber, original.PieceNumber, original.ParentPieceNumber,
            original.Material, original.Quantity, original.Weight, original.LoadingLength, "OVEN-01", "PACK-01",
            OrderPriority.Normal, DateTime.UtcNow.AddHours(1), DateTime.UtcNow, "test");
        await Repository.SaveAsync(outgoing);
        outgoing.TransitionTo(OrderStatus.OnHold, "QA", "Inspection");
        await Repository.SaveAsync(outgoing);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.StartOvenCycleAsync("OVEN-01", "DEMO-6060",
            ["AL-26031-K01", "AL-26031-K02"], "test", DateTime.UtcNow));
        var state = await Repository.GetPlantStateAsync();
        Assert.Empty(state.Cycles);
        Assert.Equal(EquipmentStatus.Available, state.Equipment.Single(e => e.Id == "OVEN-01").Status);
    }

    [Fact]
    public async Task Oven_batch_and_recipe_snapshot_are_saved_with_busy_equipment()
    {
        var cycle = await StartAsync();
        Assert.Equal(2, cycle.Load.Count);
        Assert.Equal("DEMO-6060", cycle.RecipeId);
        Assert.Equal(180m, cycle.TargetTemperature);
        var equipment = (await Repository.GetPlantStateAsync()).Equipment.Single(e => e.Id == "OVEN-01");
        Assert.Equal(EquipmentStatus.Busy, equipment.Status);
        Assert.Equal(cycle.Id.ToString(), equipment.ActiveOperation);
        Assert.Equal(7, (await Repository.GetPlantStateAsync()).Equipment.Count);
    }

    [Fact]
    public async Task Fault_and_elapsed_time_survive_restart_and_recovery_never_skips_treatment()
    {
        var cycle = await StartAsync();
        await Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 2, "test", DateTime.UtcNow);
        await ControlAsync("OVEN-01", EquipmentMode.Automatic, EquipmentCondition.Fault);
        await Initializer.InitializeAsync();
        cycle = Assert.Single((await Repository.GetPlantStateAsync()).Cycles);
        Assert.Equal(OvenPhase.Holding, cycle.Phase);
        Assert.Equal(2, cycle.ElapsedMinutes);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 1, "test", DateTime.UtcNow.AddDays(100)));
        Assert.Equal(2, Assert.Single((await Repository.GetPlantStateAsync()).Cycles).ElapsedMinutes);
        await ControlAsync("OVEN-01", EquipmentMode.Automatic, EquipmentCondition.Operational);
        await Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 1, "test", DateTime.UtcNow.AddDays(100));
        Assert.Equal(3, Assert.Single((await Repository.GetPlantStateAsync()).Cycles).ElapsedMinutes);
    }

    [Theory]
    [InlineData(EquipmentMode.Manual, EquipmentCondition.Operational)]
    [InlineData(EquipmentMode.Automatic, EquipmentCondition.Maintenance)]
    public async Task Manual_and_maintenance_modes_suspend_cycle(EquipmentMode mode, EquipmentCondition condition)
    {
        var cycle = await StartAsync();
        await ControlAsync("OVEN-01", mode, condition);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 1, "test", DateTime.UtcNow));
        Assert.Equal(0, Assert.Single((await Repository.GetPlantStateAsync()).Cycles).ElapsedMinutes);
    }

    [Fact]
    public async Task Duplicate_advance_is_rejected_and_completion_releases_oven_atomically()
    {
        var cycle = await StartAsync();
        await Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 1, "test", DateTime.UtcNow);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 1, "test", DateTime.UtcNow));
        cycle = Assert.Single((await Repository.GetPlantStateAsync()).Cycles);
        Assert.Equal(1, cycle.ElapsedMinutes);
        await Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, cycle.TotalMinutes - 1, "test", DateTime.UtcNow);
        var state = await Repository.GetPlantStateAsync();
        Assert.Equal(OvenPhase.Completed, Assert.Single(state.Cycles).Phase);
        var equipment = state.Equipment.Single(e => e.Id == "OVEN-01");
        Assert.Equal(EquipmentStatus.Available, equipment.Status);
        Assert.Null(equipment.ActiveOperation);
        Assert.Contains(equipment.History, h => h.Description.StartsWith("Completed"));
    }

    [Fact]
    public async Task Invalid_load_rolls_back_cycle_and_equipment_start()
    {
        await Initializer.InitializeAsync();
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.StartOvenCycleAsync("OVEN-01", "DEMO-6060", ["AL-26031-K01"], "test", DateTime.UtcNow));
        var state = await Repository.GetPlantStateAsync();
        Assert.Empty(state.Cycles);
        Assert.Equal(EquipmentStatus.Available, state.Equipment.Single(e => e.Id == "OVEN-01").Status);
    }

    [Fact]
    public async Task Incompatible_incoming_alloy_is_rejected_without_moving_basket()
    {
        await Initializer.InitializeAsync();
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        var order = (await Repository.GetByOrderNumberAsync("TO-2026-0003"))!;
        var transfer = Transport.Dispatch("TR-2026-0098", ExtrusionSimulator.Shuttle, [order], "test", DateTime.UtcNow);
        await Repository.SaveTransportAsync(transfer, [order]);
        var state = await Repository.GetPlantStateAsync();
        transfer = state.Transports.Single(t => t.TransportNumber == transfer.TransportNumber);
        var load = state.Orders.Where(o => o.TransportNumber == transfer.TransportNumber).ToList();
        transfer.Complete(load, "test", DateTime.UtcNow);
        await Repository.SaveTransportAsync(transfer, load);
        order = (await Repository.GetByOrderNumberAsync("TO-2026-0007"))!;
        transfer = Transport.Dispatch("TR-2026-0099", ExtrusionSimulator.Shuttle, [order], "test", DateTime.UtcNow);
        var error = await Assert.ThrowsAsync<TransportRuleException>(() => Repository.SaveTransportAsync(transfer, [order]));
        Assert.Contains("compatible recipe", error.Message);
        state = await Repository.GetPlantStateAsync();
        Assert.Equal("PRESS-02", state.Baskets.Single(b => b.Number == order.PieceNumber).LocationId);
        Assert.DoesNotContain(state.Transports, t => t.TransportNumber == "TR-2026-0099");
    }

    [Fact]
    public async Task Pending_legacy_departure_waits_for_treatment_and_cooling()
    {
        await Initializer.InitializeAsync();
        await new ExtrusionSimulator(Repository, TimeProvider.System).StepAsync();
        var original = (await Repository.GetByOrderNumberAsync("TO-2026-0001"))!;
        var outgoing = new TransportOrder("TO-2026-0099", original.HeatNumber, original.PieceNumber, original.ParentPieceNumber,
            original.Material, original.Quantity, original.Weight, original.LoadingLength, "OVEN-01", "PRESS-01",
            OrderPriority.Normal, DateTime.UtcNow.AddHours(1), DateTime.UtcNow, "test");
        await Repository.SaveAsync(outgoing);
        async Task TryDeparture()
        {
            var fresh = (await Repository.GetByOrderNumberAsync(outgoing.OrderNumber))!;
            var transfer = Transport.Dispatch("TR-2026-0099", ExtrusionSimulator.Shuttle, [fresh], "test", DateTime.UtcNow);
            await Repository.SaveTransportAsync(transfer, [fresh]);
        }
        await Assert.ThrowsAsync<TransportRuleException>(TryDeparture);
        await Repository.StartOvenCycleAsync("OVEN-01", "DEMO-6060", ["AL-26031-K01", "AL-26031-K02"], "test", DateTime.UtcNow);
        var cycle = Assert.Single((await Repository.GetPlantStateAsync()).Cycles);
        await Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 6, "test", DateTime.UtcNow);
        await Assert.ThrowsAsync<TransportRuleException>(TryDeparture);
        cycle = Assert.Single((await Repository.GetPlantStateAsync()).Cycles);
        Assert.Equal(OvenPhase.Cooling, cycle.Phase);
        await Repository.AdvanceOvenCycleAsync(cycle.Id, cycle.Version, 1, "test", DateTime.UtcNow);
        await TryDeparture();
        Assert.True((await Repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == original.PieceNumber).IsMoving);
    }

    [Fact]
    public async Task Stale_equipment_control_cannot_overwrite_fault_or_its_audit()
    {
        await Initializer.InitializeAsync();
        var equipment = (await Repository.GetPlantStateAsync()).Equipment.Single(e => e.Id == "OVEN-01");
        await ControlAsync(equipment.Id, EquipmentMode.Automatic, EquipmentCondition.Fault);
        await Assert.ThrowsAsync<TransportRuleException>(() => Repository.SetEquipmentControlAsync(equipment.Id,
            equipment.Version, EquipmentMode.Automatic, EquipmentCondition.Operational, "test", "Stale reset", DateTime.UtcNow));
        equipment = (await Repository.GetPlantStateAsync()).Equipment.Single(e => e.Id == equipment.Id);
        Assert.Equal(EquipmentCondition.Fault, equipment.Condition);
        Assert.DoesNotContain(equipment.History, h => h.Description.Contains("Stale reset"));
    }

    public void Dispose() { _services.Dispose(); _database.Dispose(); }
}
