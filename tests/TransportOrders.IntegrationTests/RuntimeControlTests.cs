using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Api;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class RuntimeControlTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private sealed class Factory(string path, bool enabled = true, bool commissioning = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Path", path);
            builder.UseSetting("Plant:Id", "runtime-test-plant");
            builder.UseSetting("Runtime:Enabled", enabled.ToString());
            builder.UseSetting("Access:UseRoles", "true");
            builder.UseSetting("Access:Roles:Supervisor:Users:0", "PLANT\\Supervisor");
            if (commissioning) builder.UseSetting("Plant:Mode", "Commissioning");
            builder.UseTestWindowsAuthentication();
        }
    }

    private static HttpClient Supervisor(Factory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Supervisor");
        return client;
    }

    private static RuntimeControlCommand Command(Guid version, bool paused, string reason = "Operator demonstration") =>
        new(version, paused, reason, "", "runtime-test-plant");

    [Fact]
    public async Task Pause_freezes_autonomous_ticks_survives_restart_and_resume_is_audited()
    {
        using var database = new TempDatabase();
        Guid pausedVersion;
        using (var factory = new Factory(database.Path))
        using (var client = Supervisor(factory))
        {
            var runtime = factory.Services.GetRequiredService<PlantRuntimeStatus>();
            // Hosted service starts asynchronously. Wait only for its ownership claim.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!runtime.Running) await Task.Delay(20, timeout.Token);
            var initial = await client.GetFromJsonAsync<PlantClientSnapshot>("/api/client-state", Json);
            var response = await client.PostAsJsonAsync("/api/runtime/control", Command(initial!.Runtime.Version, true));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var state = await response.Content.ReadFromJsonAsync<RuntimeControlState>();
            pausedVersion = state!.Version;
            Assert.True(state.Paused);
            Assert.NotEqual(initial.Runtime.Version, state.Version);
            var repeat = await client.PostAsJsonAsync("/api/runtime/control", Command(pausedVersion, true));
            Assert.Equal(pausedVersion, (await repeat.Content.ReadFromJsonAsync<RuntimeControlState>())!.Version);
            Assert.Single(await factory.Services.GetRequiredService<IRuntimeControlRepository>().HistoryAsync());
            var snapshot = await client.GetFromJsonAsync<PlantClientSnapshot>("/api/client-state", Json);
            Assert.True(snapshot!.Runtime.Running); Assert.True(snapshot.Runtime.Paused);
            // Runtime checks the persisted pause before any PLC or scheduling work.
            var tick = await factory.Services.GetRequiredService<PlantRuntime>().TickAsync();
            Assert.Equal(SimulationAction.CentralService, tick.Action);
            Assert.Equal(SimulationAction.CentralService,
                (await factory.Services.GetRequiredService<PlantCommandService>().StepAsync()).Action);
            Assert.Null(new PlantDriverLease(database.Path).TryEnter());
            var manual = await client.PostAsJsonAsync("/api/simulation/step", new DemoStepCommand("", "runtime-test-plant"));
            Assert.Contains("CentralService", await manual.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/runtime/control",
                Command(initial.Runtime.Version, false))).StatusCode);
        }

        using (var factory = new Factory(database.Path))
        using (var client = Supervisor(factory))
        {
            var runtime = factory.Services.GetRequiredService<PlantRuntimeStatus>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!runtime.Running) await Task.Delay(20, timeout.Token);
            var snapshot = await client.GetFromJsonAsync<PlantClientSnapshot>("/api/client-state", Json);
            Assert.True(snapshot!.Runtime.Paused); Assert.Equal(pausedVersion, snapshot.Runtime.Version);
            Assert.Null(runtime.LastTickUtc);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/runtime/control", Command(pausedVersion, false, "Continue production"))).StatusCode);
            var tick = await factory.Services.GetRequiredService<PlantRuntime>().TickAsync();
            Assert.NotEqual(SimulationAction.CentralService, tick.Action);
            var history = await client.GetFromJsonAsync<RuntimeControlEvent[]>("/api/runtime/history");
            Assert.Equal(2, history!.Length);
            Assert.All(history, e => { Assert.Equal("PLANT\\Supervisor", e.Actor); Assert.NotEqual(Guid.Empty, e.Version); });
            Assert.Contains(history, e => e.Paused && e.Reason == "Operator demonstration");
            Assert.Contains(history, e => !e.Paused && e.Reason == "Continue production");
        }
    }

    [Theory]
    [InlineData("", HttpStatusCode.BadRequest)]
    [InlineData("   ", HttpStatusCode.BadRequest)]
    [InlineData("wrong-plant", HttpStatusCode.Conflict)]
    public async Task Invalid_or_wrong_plant_requests_do_not_change_state(string value, HttpStatusCode expected)
    {
        using var database = new TempDatabase(); using var factory = new Factory(database.Path);
        using var client = Supervisor(factory);
        var command = value == "wrong-plant" ? Command(Guid.Parse(RuntimeControlState.InitialVersion), true) with { ExpectedPlantId = value }
            : Command(Guid.Parse(RuntimeControlState.InitialVersion), true, value);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/runtime/control", command)).StatusCode);
        Assert.False((await factory.Services.GetRequiredService<IRuntimeControlRepository>().ReadAsync()).Paused);
        Assert.Empty(await factory.Services.GetRequiredService<IRuntimeControlRepository>().HistoryAsync());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Conflict)]
    [InlineData(true, HttpStatusCode.Forbidden)]
    public async Task Disabled_runtime_and_commissioning_cannot_be_controlled(bool commissioning, HttpStatusCode expected)
    {
        using var database = new TempDatabase(); using var factory = new Factory(database.Path, false, commissioning);
        using var client = Supervisor(factory);
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/runtime/control", Command(Guid.Parse(RuntimeControlState.InitialVersion), true))).StatusCode);
    }
}
