using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransportOrders.Api;
using TransportOrders.Core;

namespace TransportOrders.IntegrationTests;

public class EquipmentControlApiTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly TempDatabase _database = new();
        public string DatabasePath => _database.Path;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Path", _database.Path);
            builder.UseSetting("EquipmentControl:AllowedOperators:0", "PLANT\\Operator");
            builder.UseTestWindowsAuthentication();
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); _database.Dispose(); }
    }
    private static HttpClient Client(Factory factory, string? actor = "PLANT\\Operator")
    {
        var client = factory.CreateClient();
        if (actor is not null) client.DefaultRequestHeaders.Add("Test-User", actor);
        return client;
    }
    private static async Task<PlantEquipment> Equipment(Factory factory) =>
        (await factory.Services.GetRequiredService<IOrderRepository>().GetPlantStateAsync()).Equipment.Single(e => e.Id == "PRESS-01");
    private static EquipmentControlRequest Request(Factory factory, PlantEquipment equipment) =>
        new(equipment.Version, EquipmentMode.Manual, EquipmentCondition.Maintenance, "Maintenance requested", factory.DatabasePath);

    [Fact]
    public async Task Command_uses_authenticated_identity_and_records_server_timestamp_and_reason()
    {
        using var factory = new Factory(); using var client = Client(factory);
        var equipment = await Equipment(factory);
        var response = await client.PostAsJsonAsync("/api/equipment/PRESS-01/control", new
        {
            ExpectedVersion = equipment.Version, Mode = EquipmentMode.Manual, Condition = EquipmentCondition.Maintenance,
            Reason = "  Maintenance requested  ", ExpectedDatabasePath = factory.DatabasePath, Operator = "FORGED"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        equipment = await Equipment(factory);
        Assert.Equal(EquipmentMode.Manual, equipment.Mode);
        Assert.Equal(EquipmentCondition.Maintenance, equipment.Condition);
        Assert.Equal("PLANT\\Operator", equipment.History.Last().By);
        Assert.Contains("Maintenance requested", equipment.History.Last().Description);
        Assert.InRange(equipment.History.Last().At, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddSeconds(1));
    }
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("PLANT\\Other", HttpStatusCode.Forbidden)]
    public async Task Anonymous_or_unlisted_operator_cannot_write(string? actor, HttpStatusCode expected)
    {
        using var factory = new Factory(); using var client = Client(factory, actor);
        var equipment = await Equipment(factory);
        var response = await client.PostAsJsonAsync("/api/equipment/PRESS-01/control", Request(factory, equipment));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(equipment.Version, (await Equipment(factory)).Version);
    }
    [Theory]
    [InlineData("reason", HttpStatusCode.BadRequest)]
    [InlineData("length", HttpStatusCode.BadRequest)]
    [InlineData("mode", HttpStatusCode.BadRequest)]
    [InlineData("condition", HttpStatusCode.BadRequest)]
    [InlineData("version", HttpStatusCode.BadRequest)]
    [InlineData("database", HttpStatusCode.Conflict)]
    public async Task Invalid_or_mismatched_command_leaves_equipment_unchanged(string field, HttpStatusCode expected)
    {
        using var factory = new Factory(); using var client = Client(factory);
        var equipment = await Equipment(factory);
        var request = Request(factory, equipment);
        request = field switch
        {
            "reason" => request with { Reason = " " }, "length" => request with { Reason = new string('x', 1001) },
            "mode" => request with { Mode = (EquipmentMode)999 }, "condition" => request with { Condition = (EquipmentCondition)999 },
            "version" => request with { ExpectedVersion = Guid.Empty }, _ => request with { ExpectedDatabasePath = "different.db" }
        };
        Assert.Equal(expected, (await client.PostAsJsonAsync("/api/equipment/PRESS-01/control", request)).StatusCode);
        Assert.Equal(equipment.Version, (await Equipment(factory)).Version);
    }
    [Fact]
    public async Task Concurrent_stations_with_same_version_apply_exactly_one_change()
    {
        using var factory = new Factory(); using var first = Client(factory); using var second = Client(factory);
        var equipment = await Equipment(factory);
        var request = Request(factory, equipment);
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/equipment/PRESS-01/control", request),
            second.PostAsJsonAsync("/api/equipment/PRESS-01/control", request with { Reason = "Second station" }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(equipment.History.Count + 1, (await Equipment(factory)).History.Count);
    }
    [Fact]
    public async Task Unknown_equipment_returns_not_found()
    {
        using var factory = new Factory(); using var client = Client(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/equipment/UNKNOWN/control",
            Request(factory, await Equipment(factory)))).StatusCode);
    }
}
