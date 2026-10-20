using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public class EquipmentControlClientTests
{
    private sealed class CaptureClient : IEquipmentControlClient
    {
        public Guid Version;
        public EquipmentMode Mode;
        public Task SetControlAsync(string id, Guid version, EquipmentMode mode, EquipmentCondition condition, string reason)
        { Version = version; Mode = mode; return Task.CompletedTask; }
    }
    [Fact]
    public async Task Background_refresh_preserves_operator_draft_and_original_version_until_explicit_reload()
    {
        var repository = new FakeOrderRepository(DateTime.UtcNow);
        var capture = new CaptureClient();
        var simulator = new ExtrusionSimulator(repository, TimeProvider.System);
        var vm = new ExtrusionViewModel(simulator, () => simulator.StepAsync(), new Localizer(), new FakeMessages(), equipmentControls: capture);
        vm.Refresh(await repository.GetPlantStateAsync());
        var original = vm.SelectedEquipment!.Version;
        vm.SelectedMode = vm.Modes.Single(m => m.Value == EquipmentMode.Manual);
        vm.ControlReason = "Operator draft";
        await repository.SetEquipmentControlAsync(vm.SelectedEquipment.Id, original, EquipmentMode.Automatic,
            EquipmentCondition.Maintenance, "other", "Another station", DateTime.UtcNow);
        vm.Refresh(await repository.GetPlantStateAsync());
        Assert.Equal(EquipmentMode.Manual, vm.SelectedMode!.Value);
        await vm.ApplyControlCommand.ExecuteAsync(null);
        Assert.Equal(original, capture.Version);
        Assert.Equal(EquipmentMode.Manual, capture.Mode);
        await vm.ReloadControlsCommand.ExecuteAsync(null);
        Assert.Equal(EquipmentMode.Automatic, vm.SelectedMode!.Value);
        Assert.Equal(EquipmentCondition.Maintenance, vm.SelectedCondition!.Value);
        Assert.Empty(vm.ControlReason);
    }
    private sealed class Handler(HttpStatusCode? status) : HttpMessageHandler
    {
        public string? Payload;
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (status is null) throw new HttpRequestException("Lost connection");
            return new(status.Value);
        }
    }
    [Fact]
    public async Task Client_sends_version_reason_and_database_without_asserting_operator_identity()
    {
        var handler = new Handler(HttpStatusCode.OK); using var http = new HttpClient(handler);
        using var client = new EquipmentControlClient("C:\\plant\\extrusion.db", new Localizer(), http);
        var version = Guid.NewGuid();
        await client.SetControlAsync("OVEN-01", version, EquipmentMode.Manual, EquipmentCondition.Maintenance, "Inspection");
        using var payload = JsonDocument.Parse(handler.Payload!);
        Assert.Equal(version, payload.RootElement.GetProperty("expectedVersion").GetGuid());
        Assert.Equal("Inspection", payload.RootElement.GetProperty("reason").GetString());
        Assert.Equal(Path.GetFullPath("C:\\plant\\extrusion.db"), payload.RootElement.GetProperty("expectedDatabasePath").GetString());
        Assert.False(payload.RootElement.TryGetProperty("operator", out _));
    }
    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(null)]
    public async Task Failed_http_command_does_not_change_local_equipment_or_retry(HttpStatusCode? status)
    {
        var repository = new FakeOrderRepository(DateTime.UtcNow);
        var before = (await repository.GetPlantStateAsync()).Equipment.Single();
        var handler = new Handler(status); using var http = new HttpClient(handler);
        using var client = new EquipmentControlClient("C:\\plant\\extrusion.db", new Localizer(), http);
        var messages = new FakeMessages();
        var simulator = new ExtrusionSimulator(repository, TimeProvider.System);
        var vm = new ExtrusionViewModel(simulator, () => simulator.StepAsync(), new Localizer(), messages, equipmentControls: client);
        vm.Refresh(await repository.GetPlantStateAsync());
        vm.ControlReason = "Inspection";
        await vm.ToggleFaultCommand.ExecuteAsync(null);
        Assert.Single(messages.Errors);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(before.Version, (await repository.GetPlantStateAsync()).Equipment.Single().Version);
        Assert.Equal("Inspection", vm.ControlReason);
        Assert.False(vm.IsBusy);
    }
}
