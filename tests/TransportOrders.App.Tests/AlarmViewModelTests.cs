using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public class AlarmViewModelTests
{
    private sealed class Handler(PlantClientSnapshot snapshot) : HttpMessageHandler
    {
        public Guid SentVersion;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = JsonContent.Create(snapshot) };
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            SentVersion = json.RootElement.GetProperty("expectedVersion").GetGuid();
            return new(HttpStatusCode.Conflict);
        }
    }
    [Fact]
    public async Task Polling_preserves_draft_version_comment_and_disables_acknowledgement_when_offline()
    {
        var alarm = PlantAlarmSnapshot.From(new PlantAlarm(new("E", AlarmCode.EquipmentFault, "OVEN-01", ""), DateTime.UtcNow));
        var snapshot = new PlantClientSnapshot(1, "site", "Plant", "UTC", DateTime.UtcNow, PlantStateSnapshot.From(new([], [], [], [], [])),
            new("Operator", ["Maintenance"], [PlantPermissions.Equipment], ""), new(true, true, DateTime.UtcNow, null), [alarm]);
        var handler = new Handler(snapshot); using var client = new HttpClient(handler);
        using var connection = new PlantConnection("https://localhost", client: client);
        await connection.ReadAsync();
        var messages = new FakeMessages(); var localizer = new Localizer();
        var vm = new AlarmViewModel(connection, localizer, messages, () => Task.CompletedTask);
        vm.Refresh(snapshot); vm.Reason = "Checked physical equipment";
        var changed = alarm with { Version = Guid.NewGuid() };
        vm.Refresh(snapshot with { Alarms = [changed] });
        Assert.Equal("Checked physical equipment", vm.Reason);
        await vm.AcknowledgeCommand.ExecuteAsync(null);
        Assert.Equal(alarm.Version, handler.SentVersion);
        vm.RefreshSelectionCommand.Execute(null);
        await vm.AcknowledgeCommand.ExecuteAsync(null);
        Assert.Equal(changed.Version, handler.SentVersion);
        vm.Refresh(null); Assert.False(vm.AcknowledgeCommand.CanExecute(null));
        localizer.SetLanguage("de"); Assert.Equal("Anlagenstörung", vm.Alarms.Single().Message);
    }
}
