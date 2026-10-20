using Microsoft.Extensions.Logging.Abstractions;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public class ExtrusionInteractionTests
{
    private sealed class TestControls(FakeOrderRepository repository) : IEquipmentControlClient
    {
        public Task SetControlAsync(string id, Guid version, EquipmentMode mode, EquipmentCondition condition, string reason) =>
            repository.SetEquipmentControlAsync(id, version, mode, condition, Environment.UserName, reason, DateTime.UtcNow);
    }
    private static readonly DateTime Created = DateTime.UtcNow.AddHours(-1);

    [Fact]
    public async Task Equipment_controls_require_reason_and_persist_manual_mode()
    {
        var repository = new FakeOrderRepository(Created);
        var simulator = new ExtrusionSimulator(repository, TimeProvider.System);
        var messages = new FakeMessages();
        var vm = new ExtrusionViewModel(simulator, () => simulator.StepAsync(), new Localizer(), messages,
            equipmentControls: new TestControls(repository));
        vm.Refresh(await repository.GetPlantStateAsync());
        vm.SelectedMode = vm.Modes.Single(m => m.Value == EquipmentMode.Manual);
        await vm.ApplyControlCommand.ExecuteAsync(null);
        Assert.Single(messages.Errors);
        Assert.Equal(EquipmentMode.Automatic, (await repository.GetPlantStateAsync()).Equipment.Single().Mode);
        vm.ControlReason = "Operator intervention";
        await vm.ApplyControlCommand.ExecuteAsync(null);
        Assert.Equal(EquipmentMode.Manual, (await repository.GetPlantStateAsync()).Equipment.Single().Mode);
        Assert.Contains("Manual", vm.EquipmentState);
        Assert.Equal(SimulationAction.Waiting, (await simulator.StepAsync()).Action);
    }

    [Fact]
    public async Task Failed_fault_save_keeps_dashboard_on_the_persisted_equipment_state()
    {
        var repository = new FakeOrderRepository(Created);
        var simulator = new ExtrusionSimulator(repository, TimeProvider.System);
        var messages = new FakeMessages();
        var vm = new ExtrusionViewModel(simulator, () => simulator.StepAsync(), new Localizer(), messages,
            equipmentControls: new TestControls(repository));
        vm.Refresh(await repository.GetPlantStateAsync());
        repository.FailNextSave = new InvalidOperationException("Database unavailable");
        await vm.ToggleFaultCommand.ExecuteAsync(null);
        Assert.False(vm.IsFaulted);
        Assert.False(vm.IsBusy);
        Assert.Single(messages.Errors);
        Assert.Equal(EquipmentCondition.Operational, (await repository.GetPlantStateAsync()).Equipment.Single().Condition);
    }

    [Fact]
    public async Task Manual_dispatch_cannot_bypass_an_existing_destination_reservation()
    {
        var repository = new FakeOrderRepository(Created)
            .Add("TO-2026-0001", "AL-001", "AL-001-K01", null, 6.4m, 1m, "OVEN-01", "PACK-01")
            .Add("TO-2026-0002", "AL-001", "AL-001-K02", null, 6.4m, 1m, "OVEN-01", "PACK-01")
            .Ship("TR-2026-0001", ExtrusionSimulator.Shuttle, null, "TO-2026-0001");
        var messages = new FakeMessages();
        var vm = Create(repository, messages);
        await vm.LoadAsync();
        var order = vm.VisibleOrders.Single(o => o.OrderNumber == "TO-2026-0002").Order;
        var number = await vm.DispatchAsync(new LoadProposal(ExtrusionSimulator.Shuttle, "OVEN-01", "PACK-01", [order], false), null);
        Assert.Null(number);
        Assert.Single(messages.Errors);
        Assert.Equal(0, repository.SaveCount);
        vm.SelectedOrder = vm.VisibleOrders.Single(o => o.Status == OrderStatus.InTransit);
        Assert.False(vm.CancelOrderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Scanning_a_basket_selects_its_latest_transport_order()
    {
        var repository = new FakeOrderRepository(Created)
            .Add("TO-2026-0001", "AL-001", "AL-001-K01", null, 6.4m, 1m)
            .Add("TO-2026-0002", "AL-001", "AL-001-K01", null, 6.4m, 1m);
        var vm = Create(repository, new FakeMessages());
        await vm.LoadAsync();
        vm.ScanText = " al-001-k01 ";
        vm.ScanCommand.Execute(null);
        Assert.Equal("TO-2026-0002", vm.SelectedOrder!.OrderNumber);
        vm.SearchText = "al-001-k01";
        Assert.Equal(2, vm.VisibleOrders.Count);
    }

    [Fact]
    public async Task Waiting_stops_automatic_mode_and_fault_requires_explicit_recovery()
    {
        var repository = new FakeOrderRepository(Created);
        var simulator = new ExtrusionSimulator(repository, TimeProvider.System);
        var vm = new ExtrusionViewModel(simulator, () => simulator.StepAsync(), new Localizer(), new FakeMessages(),
            equipmentControls: new TestControls(repository));
        vm.Refresh(await repository.GetPlantStateAsync());
        vm.ToggleRunCommand.Execute(null);
        Assert.False(vm.CanEditOrders);
        await vm.StepCommand.ExecuteAsync(null);
        Assert.False(vm.IsRunning);
        Assert.True(vm.CanEditOrders);
        await vm.ToggleFaultCommand.ExecuteAsync(null);
        Assert.True(vm.IsFaulted);
        await vm.StepCommand.ExecuteAsync(null);
        Assert.Contains("Shuttle fault", vm.LastEvent);
        await vm.ToggleFaultCommand.ExecuteAsync(null);
        Assert.False(vm.IsFaulted);
        Assert.False(vm.IsRunning);
    }

    private static MainViewModel Create(IOrderRepository repository, FakeMessages messages) =>
        new(repository, messages, new FakeDialogs(), new Localizer(), new FakeUser("test"),
            TimeProvider.System, TimeZoneInfo.Utc, NullLogger<MainViewModel>.Instance);
}
