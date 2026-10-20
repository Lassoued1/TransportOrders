using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TransportOrders.IntegrationTests;

public class CommissioningTests
{
    [Fact]
    public async Task Commissioning_creates_no_demo_orders_or_equipment_and_refuses_simulation_and_operator_writes()
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Plant:Mode", "Commissioning"); builder.UseSetting("Plant:Id", "real-site-commissioning");
            builder.UseSetting("Runtime:Enabled", "false");
            builder.UseSetting("Access:UseRoles", "true"); builder.UseSetting("Access:Roles:Supervisor:Users:0", "PLANT\\Reader");
        });
        using var client = factory.CreateClient();
        var snapshot = await client.GetFromJsonAsync<JsonElement>("/api/client-state");
        Assert.Equal("Commissioning", snapshot.GetProperty("operatingMode").GetString());
        Assert.Equal(0, snapshot.GetProperty("access").GetProperty("permissions").GetArrayLength());
        foreach (var name in new[] { "orders", "baskets", "equipment", "recipes", "plcCommands" })
            Assert.Equal(0, snapshot.GetProperty("state").GetProperty(name).GetArrayLength());
        foreach (var route in new[] { "/api/simulation/step", "/api/equipment/E/control", "/api/plc/simulation/settings",
            "/api/alarms/11111111-1111-1111-1111-111111111111/acknowledge", "/api/recipes/NEW/create" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
    [Theory]
    [InlineData("Live", false)]
    [InlineData("Commissioning", true)]
    public void Unsupported_live_mode_or_enabled_demo_runtime_are_refused_before_initializing_the_database(string mode, bool runtime)
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Plant:Mode", mode); builder.UseSetting("Plant:Id", "real-site");
            builder.UseSetting("Runtime:Enabled", runtime.ToString());
        });
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.False(File.Exists(baseFactory.DatabasePath));
    }
}
