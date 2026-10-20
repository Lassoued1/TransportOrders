using TransportOrders.Core;

namespace TransportOrders.Tests;

public sealed class OvenCycleTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 6, 0, 0, DateTimeKind.Utc);
    private static OvenCycle Cycle() => new("OVEN-01", new("DEMO-6060", "6060", 180m, 2, 3, 2),
        [new("AL-1-K01", "AL-1-B01", 10, 1m, 6m, "OVEN-01")], [new("AL-1-B01", "AL-1", "EN AW-6060 profiles")], Now);

    [Theory]
    [InlineData(1, OvenPhase.Heating, 100)]
    [InlineData(2, OvenPhase.Holding, 180)]
    [InlineData(4, OvenPhase.Holding, 180)]
    [InlineData(5, OvenPhase.Cooling, 180)]
    [InlineData(6, OvenPhase.Cooling, 100)]
    [InlineData(7, OvenPhase.Completed, 20)]
    public void Phase_and_temperature_follow_the_complete_recipe(int minutes, OvenPhase phase, int temperature)
    {
        var cycle = Cycle();
        cycle.Advance(minutes, Now.AddSeconds(2));
        Assert.Equal(phase, cycle.Phase);
        Assert.Equal(temperature, cycle.SimulatedTemperature);
        Assert.Equal(phase == OvenPhase.Completed ? Now.AddSeconds(2) : (DateTime?)null, cycle.CompletedAt);
    }

    [Fact]
    public void Completed_cycle_cannot_be_advanced_or_release_extra_time()
    {
        var cycle = Cycle();
        cycle.Advance(100, Now);
        Assert.Equal(cycle.TotalMinutes, cycle.ElapsedMinutes);
        Assert.Throws<TransportRuleException>(() => cycle.Advance(1, Now));
    }

    [Fact]
    public void Incompatible_alloy_cannot_enter_cycle()
    {
        Assert.Throws<TransportRuleException>(() => new OvenCycle("OVEN-01", new("DEMO-6060", "6060", 180m, 2, 3, 2),
            [new("AL-1-K01", "AL-1-B01", 10, 1m, 6m, "OVEN-01")], [new("AL-1-B01", "AL-1", "EN AW-6082 profiles")], Now));
        Assert.False(new OvenRecipe("DEMO-6060", "6060", 180m, 2, 3, 2).Supports(new("L", "B", "EN AW-60601 profiles")));
    }

    [Fact]
    public void Recovering_equipment_preserves_active_operation()
    {
        var equipment = new PlantEquipment("OVEN-01", EquipmentKind.Oven, "OVEN-01", Now);
        equipment.Start("cycle", "test", Now);
        equipment.SetControl(EquipmentMode.Automatic, EquipmentCondition.Fault, "test", "Fault", Now);
        Assert.Equal(EquipmentStatus.Fault, equipment.Status);
        Assert.Throws<TransportRuleException>(() => equipment.Finish("cycle", "test", Now));
        equipment.SetControl(EquipmentMode.Automatic, EquipmentCondition.Operational, "test", "Recovered", Now);
        Assert.Equal(EquipmentStatus.Busy, equipment.Status);
        Assert.Equal("cycle", equipment.ActiveOperation);
        equipment.Finish("cycle", "test", Now);
        Assert.Equal(EquipmentStatus.Available, equipment.Status);
    }
}
