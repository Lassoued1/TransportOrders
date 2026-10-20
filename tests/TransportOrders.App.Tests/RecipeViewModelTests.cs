using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using TransportOrders.App.Services;
using TransportOrders.App.ViewModels;
using TransportOrders.Core;
namespace TransportOrders.App.Tests;

public class RecipeViewModelTests
{
    private sealed class Handler(PlantClientSnapshot snapshot) : HttpMessageHandler
    {
        public Guid SentVersion;
        public decimal SentTemperature;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = JsonContent.Create(snapshot) };
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            SentVersion = json.RootElement.GetProperty("expectedVersion").GetGuid();
            if (json.RootElement.GetProperty("parameters").ValueKind == JsonValueKind.Object)
                SentTemperature = json.RootElement.GetProperty("parameters").GetProperty("targetTemperature").GetDecimal();
            return new(HttpStatusCode.Conflict);
        }
    }
    [Fact]
    public async Task Poll_preserves_unsaved_values_and_version_and_validation_requires_saved_parameters()
    {
        var recipe = OvenRecipeSnapshot.From(OvenRecipe.CreateDraft("TEST", new("6060", 180, 2, 3, 2), "test", "Created", DateTime.UtcNow));
        var snapshot = new PlantClientSnapshot(1, "site", "Plant", "UTC", DateTime.UtcNow,
            PlantStateSnapshot.From(new([], [], [], [], [])),
            new("Operator", ["Supervisor"], [PlantPermissions.Equipment], ""), new(true, true, DateTime.UtcNow, null));
        snapshot = snapshot with { State = snapshot.State with { Recipes = [recipe] } };
        var handler = new Handler(snapshot); using var client = new HttpClient(handler);
        using var connection = new PlantConnection("https://localhost", client: client); await connection.ReadAsync();
        var localizer = new Localizer(); localizer.SetLanguage("de");
        var vm = new RecipeViewModel(connection, localizer, new FakeMessages(), () => Task.CompletedTask);
        vm.Refresh(snapshot); vm.Reason = "Prüfung";
        Assert.True(vm.ValidateCommand.CanExecute(null)); Assert.True(vm.IdentifierReadOnly);
        vm.Temperature = "185,5"; Assert.False(vm.ValidateCommand.CanExecute(null)); Assert.False(vm.ArchiveCommand.CanExecute(null));
        vm.Refresh(snapshot with { State = snapshot.State with { Recipes = [recipe with { Version = Guid.NewGuid() }] } });
        Assert.Equal("185,5", vm.Temperature); Assert.Equal("Prüfung", vm.Reason);
        await vm.SaveCommand.ExecuteAsync(null); Assert.Equal(recipe.Version, handler.SentVersion); Assert.Equal(185.5m, handler.SentTemperature);
        vm.Refresh(null); Assert.False(vm.SaveCommand.CanExecute(null)); Assert.False(vm.ValidateCommand.CanExecute(null));
        vm.Refresh(snapshot with { Access = snapshot.Access with { Roles = ["Maintenance"] } }); Assert.False(vm.NewCommand.CanExecute(null));
    }
}
