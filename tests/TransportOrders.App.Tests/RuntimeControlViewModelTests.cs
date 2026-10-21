using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public sealed class RuntimeControlViewModelTests
{
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public RuntimeControlCommand? Command;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/api/runtime/control", request.RequestUri!.AbsolutePath);
            Command = await request.Content!.ReadFromJsonAsync<RuntimeControlCommand>(ct);
            return new(status) { Content = JsonContent.Create(new RuntimeControlState { Paused = true, Version = Guid.NewGuid() }) };
        }
    }

    [Fact]
    public async Task Central_controls_require_permission_and_connection_and_preserve_draft_version_on_refresh()
    {
        var repository = new FakeOrderRepository(DateTime.UtcNow.AddHours(-1));
        var handler = new Handler(HttpStatusCode.Conflict);
        using var http = new HttpClient(handler);
        using var client = new OperatorCommandClient("C:\\plant\\extrusion.db", new Localizer(), http);
        var messages = new FakeMessages();
        var simulator = new ExtrusionSimulator(repository, TimeProvider.System);
        var vm = new ExtrusionViewModel(simulator, () => simulator.StepAsync(), new Localizer(), messages, operatorCommands: client);
        var version = Guid.NewGuid();
        vm.SetRuntimeState(new(true, true, null, null, false, version));
        Assert.False(vm.StepCommand.CanExecute(null));
        Assert.False(vm.ToggleRunCommand.CanExecute(null));
        Assert.False(vm.ToggleCentralRuntimeCommand.CanExecute(null));
        vm.RuntimeReason = "Demonstration pause";
        vm.SetPermissions([PlantPermissions.Equipment]);
        Assert.False(vm.ToggleCentralRuntimeCommand.CanExecute(null));
        vm.SetPermissions([PlantPermissions.Runtime]);
        Assert.True(vm.ToggleCentralRuntimeCommand.CanExecute(null));
        vm.SetRuntimeState(new(true, true, null, null, false, Guid.NewGuid()));
        await vm.ToggleCentralRuntimeCommand.ExecuteAsync(null);
        Assert.Equal(version, handler.Command!.ExpectedVersion);
        Assert.True(handler.Command.Paused);
        Assert.Equal("Demonstration pause", vm.RuntimeReason);
        Assert.Single(messages.Errors);
        vm.SetRuntimeState(null);
        Assert.False(vm.ToggleCentralRuntimeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Successful_resume_clears_reason_and_refreshes_without_claiming_local_run()
    {
        var repository = new FakeOrderRepository(DateTime.UtcNow.AddHours(-1));
        var handler = new Handler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        using var client = new OperatorCommandClient("C:\\plant\\extrusion.db", new Localizer(), http);
        var messages = new FakeMessages(); var refreshes = 0;
        var simulator = new ExtrusionSimulator(repository, TimeProvider.System);
        var vm = new ExtrusionViewModel(simulator, () => simulator.StepAsync(), new Localizer(), messages,
            () => { refreshes++; return Task.CompletedTask; }, operatorCommands: client);
        vm.SetRuntimeState(new(true, true, null, null, true, Guid.NewGuid()));
        vm.RuntimeReason = "Continue demonstration";
        Assert.Contains("Resume", vm.CentralRuntimeLabel);
        await vm.ToggleCentralRuntimeCommand.ExecuteAsync(null);
        Assert.False(handler.Command!.Paused);
        Assert.Equal("", vm.RuntimeReason); Assert.Equal(1, refreshes);
        Assert.False(vm.IsRunning); Assert.False(vm.IsBusy); Assert.Empty(messages.Errors);
    }
}
