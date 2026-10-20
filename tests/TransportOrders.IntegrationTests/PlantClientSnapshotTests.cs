using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public class PlantClientSnapshotTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    [Fact]
    public async Task Json_roundtrip_preserves_physical_state_history_versions_and_order_revisions()
    {
        using var database = new TempDatabase();
        using var services = new ServiceCollection().AddTransportOrdersData(database.Path).BuildServiceProvider();
        await services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        var repository = services.GetRequiredService<IOrderRepository>();
        var simulator = new ExtrusionSimulator(repository, TimeProvider.System);
        await simulator.StepAsync();
        var state = await repository.GetPlantStateAsync();
        var basket = state.Baskets.Single(b => b.Number == "AL-26052-K01");
        await repository.StartPackingAsync(basket.Number, basket.Version, "test", DateTime.UtcNow);
        var package = Assert.Single((await repository.GetPlantStateAsync()).Packages);
        await repository.CompletePackingAsync(package.Number, package.Version, "test", DateTime.UtcNow);
        package = Assert.Single((await repository.GetPlantStateAsync()).Packages);
        await repository.InspectPackageAsync(package.Number, package.Version, true, true, "QA", "Checks passed", DateTime.UtcNow);
        state = await repository.GetPlantStateAsync();
        var encoded = JsonSerializer.Serialize(PlantStateSnapshot.From(state), Json);
        var restored = JsonSerializer.Deserialize<PlantStateSnapshot>(encoded, Json)!.Restore();
        Assert.Equal(encoded, JsonSerializer.Serialize(PlantStateSnapshot.From(restored), Json));
        Assert.Equal(state.Orders.Select(OrderRevision.For), restored.Orders.Select(OrderRevision.For));
        Assert.Equal(state.Locate().Select(b => b.BasketNumber), restored.Locate().Select(b => b.BasketNumber));
        Assert.NotEmpty(restored.PlcCommands);
        Assert.NotEmpty(Assert.Single(restored.Packages).History);
    }
    [Fact]
    public async Task Snapshot_is_authenticated_and_site_id_replaces_server_file_paths_in_commands()
    {
        using var factory = new RoleApiFactory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/client-state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Supervisor");
        var snapshot = (await client.GetFromJsonAsync<PlantClientSnapshot>("/api/client-state", Json))!;
        Assert.Equal(1, snapshot.ContractVersion);
        Assert.Equal("", snapshot.Access.DatabasePath);
        var order = snapshot.State.Restore().Orders.First(o => o.Status == OrderStatus.Planned);
        var route = $"/api/orders/{order.OrderNumber}/state";
        var command = new OrderStateCommand(OrderRevision.For(order), OrderStatus.OnHold, "Network check", "", snapshot.PlantId);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, command with { ExpectedPlantId = "another-plant" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route, command)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, command)).StatusCode);
        var after = (await client.GetFromJsonAsync<PlantClientSnapshot>("/api/client-state", Json))!;
        Assert.Equal(OrderStatus.OnHold, after.State.Restore().Orders.Single(o => o.OrderNumber == order.OrderNumber).Status);
    }
}
