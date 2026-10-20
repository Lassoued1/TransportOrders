using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public class OperatorAccessTests
{
    private static async Task<MainViewModel> ViewModel()
    {
        var repository = new FakeOrderRepository(DateTime.UtcNow.AddHours(-1))
            .Add("TO-2026-0001", "LOT-1", "LOT-1-B", null, 6m, 1m);
        var vm = new MainViewModel(repository, new FakeMessages(), new FakeDialogs(), new Localizer(), new FakeUser("test"),
            TimeProvider.System, TimeZoneInfo.Utc, NullLogger<MainViewModel>.Instance);
        vm.InitializeExtrusion(new ExtrusionSimulator(repository, TimeProvider.System));
        await vm.LoadAsync(); vm.SelectedOrder = vm.VisibleOrders.Single(); return vm;
    }
    [Fact]
    public async Task Quality_operator_can_hold_orders_but_cannot_dispatch_cancel_or_control_equipment()
    {
        var vm = await ViewModel();
        vm.SetOperatorAccess(new("PLANT\\QA", ["Quality"], [PlantPermissions.Quality, PlantPermissions.OrderQuality], "plant.db"));
        Assert.True(vm.HoldOrReleaseCommand.CanExecute(null)); Assert.False(vm.CancelOrderCommand.CanExecute(null));
        Assert.False(vm.PlanLoadsCommand.CanExecute(null)); Assert.False(vm.Extrusion!.ApplyControlCommand.CanExecute(null));
        Assert.Contains("Quality", vm.OperatorStatus);
    }
    [Fact]
    public async Task Maintenance_operator_can_control_equipment_but_cannot_start_simulation()
    {
        var vm = await ViewModel();
        vm.SetOperatorAccess(new("PLANT\\Tech", ["Maintenance"], [PlantPermissions.Equipment, PlantPermissions.Plc], "plant.db"));
        Assert.True(vm.Extrusion!.ApplyControlCommand.CanExecute(null)); Assert.True(vm.Extrusion.ToggleFaultCommand.CanExecute(null));
        Assert.False(vm.Extrusion.ToggleRunCommand.CanExecute(null)); Assert.False(vm.Extrusion.StepCommand.CanExecute(null));
        Assert.True(vm.Extrusion.ReloadControlsCommand.CanExecute(null));
    }
    [Fact]
    public async Task Loss_of_access_disables_actions_and_stops_desktop_run()
    {
        var vm = await ViewModel(); vm.SetOperatorAccess(new("PLANT\\Boss", ["Supervisor"], PlantPermissions.All, "plant.db"));
        vm.Extrusion!.ToggleRunCommand.Execute(null); Assert.True(vm.Extrusion.IsRunning);
        vm.SetOperatorAccess(null);
        Assert.False(vm.Extrusion.IsRunning); Assert.False(vm.Extrusion.StepCommand.CanExecute(null));
        Assert.False(vm.Extrusion.ApplyControlCommand.CanExecute(null)); Assert.False(vm.PlanLoadsCommand.CanExecute(null));
        Assert.False(vm.HoldOrReleaseCommand.CanExecute(null)); Assert.Contains("disabled", vm.OperatorStatus);
    }
    private sealed class Handler(OperatorAccessSnapshot? snapshot) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot is null ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(snapshot) });
    }
    [Theory]
    [InlineData(null)][InlineData("C:\\other\\plant.db")]
    public async Task Unauthenticated_or_wrong_database_access_snapshot_is_not_used(string? path)
    {
        using var http = new HttpClient(new Handler(path is null ? null : new("user", ["Supervisor"], PlantPermissions.All, path)));
        using var monitor = new OperatorAccessMonitor("C:\\plant\\plant.db", http);
        Assert.Null(await monitor.ReadAsync());
    }
}
