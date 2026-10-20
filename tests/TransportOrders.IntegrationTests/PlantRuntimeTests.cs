using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class PlantRuntimeTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow.AddHours(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Runtime_receives_and_expires_commands_while_scheduler_is_blocked_then_recovers_after_restart()
    {
        using var database = new TempDatabase();
        var clock = new Clock();
        using (var services = new ServiceCollection().AddTransportOrdersData(database.Path).BuildServiceProvider())
        {
            await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            var repo = services.GetRequiredService<IOrderRepository>();
            var state = await repo.GetPlantStateAsync();
            var settings = state.PlcSimulation!;
            await ((IPlcRepository)repo).SetPlcSimulationAsync(settings.Version, true, true, "test", "Lost reply", clock.Now.UtcDateTime);
            var shuttle = state.Equipment.Single(e => e.Id == PlantEquipment.SharedShuttle);
            await repo.SetEquipmentControlAsync(shuttle.Id, shuttle.Version, EquipmentMode.Automatic,
                EquipmentCondition.Fault, "test", "Blocked equipment", clock.Now.UtcDateTime);
            var runtime = new PlantRuntime(repo, clock);
            await runtime.TickAsync();
            Assert.Equal(PlcCommandStatus.AwaitingAck, Assert.Single((await repo.GetPlantStateAsync()).PlcCommands).Status);
            clock.Now = clock.Now.AddSeconds(16);
            await runtime.TickAsync();
            Assert.Equal(PlcCommandStatus.TimedOut, Assert.Single((await repo.GetPlantStateAsync()).PlcCommands).Status);
        }
        using (var services = new ServiceCollection().AddTransportOrdersData(database.Path).BuildServiceProvider())
        {
            await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            var repo = services.GetRequiredService<IOrderRepository>();
            var state = await repo.GetPlantStateAsync();
            await ((IPlcRepository)repo).SetPlcSimulationAsync(state.PlcSimulation!.Version, true, false,
                "test", "Replies restored", clock.Now.UtcDateTime);
            var runtime = new PlantRuntime(repo, clock);
            await runtime.TickAsync();
            state = await repo.GetPlantStateAsync();
            Assert.Equal(PlcCommandStatus.Completed, Assert.Single(state.PlcCommands).Status);
            Assert.Contains(state.Baskets, b => b.IsMoving);
            var shuttle = state.Equipment.Single(e => e.Id == PlantEquipment.SharedShuttle);
            await repo.SetEquipmentControlAsync(shuttle.Id, shuttle.Version, EquipmentMode.Automatic,
                EquipmentCondition.Operational, "test", "Fault cleared", clock.Now.UtcDateTime);
            await runtime.TickAsync();
            var original = (await repo.GetPlantStateAsync()).PlcCommands.Single(c => c.Kind == PlcCommandKind.MaterialTransfer);
            Assert.Equal(PlcCommandStatus.Applied, original.Status);
            Assert.Equal(1, original.Attempts);
        }
    }

    [Fact]
    public async Task Service_lease_blocks_desktop_scheduler_and_is_released_on_shutdown()
    {
        using var database = new TempDatabase();
        using var services = new ServiceCollection().AddTransportOrdersData(database.Path).BuildServiceProvider();
        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var repo = services.GetRequiredService<IOrderRepository>();
        var lease = new PlantDriverLease(database.Path);
        var desktop = new ExtrusionSimulator(repo, TimeProvider.System, executionGate: lease);
        using (var owned = lease.TryEnter())
        {
            Assert.NotNull(owned);
            Assert.Null(new PlantDriverLease(database.Path).TryEnter());
            Assert.Equal(SimulationAction.CentralService, (await desktop.StepAsync()).Action);
            Assert.Equal(PlcCommandStatus.Pending, Assert.Single((await repo.GetPlantStateAsync()).PlcCommands).Status);
        }
        Assert.Equal(SimulationAction.Arrived, (await desktop.StepAsync()).Action);
    }
}
