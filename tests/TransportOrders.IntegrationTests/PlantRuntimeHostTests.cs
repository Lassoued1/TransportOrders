using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class PlantRuntimeHostTests
{
    private sealed class Factory(string path) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Path", path);
            builder.UseSetting("Runtime:Enabled", "true");
            builder.UseSetting("OperatorCommands:AllowedOperators:0", "PLANT\\Operator");
            builder.UseTestWindowsAuthentication();
        }
    }

    [Fact]
    public async Task Hosted_runtime_runs_without_Wpf_and_releases_ownership_on_stop()
    {
        using var database = new TempDatabase();
        using (var factory = new Factory(database.Path))
        using (var client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Operator");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            JsonElement status;
            do
            {
                status = await client.GetFromJsonAsync<JsonElement>("/api/runtime");
                if (status.GetProperty("lastTickUtc").ValueKind != JsonValueKind.Null) break;
                await Task.Delay(50);
            } while (DateTime.UtcNow < deadline);
            Assert.True(status.GetProperty("enabled").GetBoolean());
            Assert.True(status.GetProperty("running").GetBoolean());
            Assert.Equal(database.Path, status.GetProperty("databasePath").GetString());
            Assert.NotEqual(JsonValueKind.Null, status.GetProperty("lastTickUtc").ValueKind);
            Assert.Equal(JsonValueKind.Null, status.GetProperty("lastError").ValueKind);
            var step = await client.PostAsJsonAsync("/api/simulation/step", new DemoStepCommand(database.Path));
            Assert.Equal(System.Net.HttpStatusCode.OK, step.StatusCode);
            Assert.Contains("CentralService", await step.Content.ReadAsStringAsync());
            Assert.Null(new PlantDriverLease(database.Path).TryEnter());
            var repo = factory.Services.GetRequiredService<IOrderRepository>();
            Assert.Contains((await repo.GetPlantStateAsync()).PlcCommands, c => c.Status == PlcCommandStatus.Applied);
        }
        using var released = new PlantDriverLease(database.Path).TryEnter();
        Assert.NotNull(released);
    }
}
