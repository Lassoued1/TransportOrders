using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Api;
using TransportOrders.Core;

namespace TransportOrders.IntegrationTests;

/// <summary>Runs the real API in memory against its own temporary SQLite file.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TempDatabase _database = new();

    public string DatabasePath => _database.Path;
    protected override void ConfigureClient(HttpClient client)
    { base.ConfigureClient(client); client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Reader"); }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Path", _database.Path);
        builder.UseSetting("Plant:TimeZone", "UTC");
        builder.UseTestWindowsAuthentication();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _database.Dispose();
    }
}

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Api_uses_the_configured_database_file()
    {
        await _client.GetAsync("/health");

        Assert.True(File.Exists(factory.DatabasePath));
    }

    [Fact]
    public async Task Lists_all_seeded_orders_with_statuses_as_text()
    {
        var response = await _client.GetAsync("/api/orders");
        var body = await response.Content.ReadAsStringAsync();
        var orders = JsonSerializer.Deserialize<List<OrderDto>>(body, Json)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, orders.Count);
        Assert.Contains("\"status\":\"Planned\"", body);
        Assert.Contains("\"status\":\"OnHold\"", body);
        Assert.All(orders, o => Assert.True(o.WeightTonnes > 0 && o.LoadingLengthMetres > 0));
    }

    [Theory]
    [InlineData("Planned", 7)]
    [InlineData("onhold", 1)]
    [InlineData("intransit", 1)]
    [InlineData("Done", 2)]
    [InlineData("Cancelled", 1)]
    public async Task Filters_by_status(string status, int expected)
    {
        var orders = await _client.GetFromJsonAsync<List<OrderDto>>($"/api/orders?status={status}", Json);

        Assert.Equal(expected, orders!.Count);
    }

    [Fact]
    public async Task Filters_by_heat_number_for_traceability()
    {
        var orders = await _client.GetFromJsonAsync<List<OrderDto>>("/api/orders?heat=al-26031", Json);

        Assert.NotEmpty(orders!);
        Assert.All(orders!, o => Assert.Equal("AL-26031", o.HeatNumber));
        Assert.All(orders!, o => Assert.Equal("EN AW-6060 profiles", o.Material));
    }

    [Fact]
    public async Task Filters_overdue_orders()
    {
        var orders = await _client.GetFromJsonAsync<List<OrderDto>>("/api/orders?overdue=true", Json);

        Assert.Equal(2, orders!.Count);
        Assert.All(orders!, o => Assert.True(o.IsOverdue));
    }

    [Fact]
    public async Task Unknown_status_returns_a_validation_problem()
    {
        var response = await _client.GetAsync("/api/orders?status=Lost");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Unknown status 'Lost'", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Order_details_include_the_history()
    {
        var all = await _client.GetFromJsonAsync<List<OrderDto>>("/api/orders?status=Done", Json);

        var detail = await _client.GetFromJsonAsync<OrderDetailDto>($"/api/orders/{all![0].Id}", Json);
        var history = await _client.GetFromJsonAsync<List<StatusChangeDto>>($"/api/orders/{all[0].Id}/history", Json);

        Assert.Equal(all[0].OrderNumber, detail!.Order.OrderNumber);
        Assert.Equal([OrderStatus.Planned, OrderStatus.InTransit, OrderStatus.Done], history!.Select(h => h.ToStatus));
        Assert.Equal(history!.Count, detail.History.Count);
    }

    [Fact]
    public async Task Order_can_be_found_by_scanned_number()
    {
        var detail = await _client.GetFromJsonAsync<OrderDetailDto>("/api/orders/by-number/to-2026-0002", Json);

        Assert.Equal("TO-2026-0002", detail!.Order.OrderNumber);
    }

    [Theory]
    [InlineData("/api/orders/9999")]
    [InlineData("/api/orders/9999/history")]
    [InlineData("/api/orders/by-number/TO-2026-9999")]
    public async Task Unknown_order_returns_404(string url)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Shift_summary_reports_open_and_overdue_orders()
    {
        var summary = await _client.GetFromJsonAsync<ShiftSummaryDto>("/api/shift-summary", Json);

        Assert.Equal("UTC", summary!.TimeZone);
        Assert.Equal((9, 2, 3, 1), (summary.OpenOrders, summary.OverdueOrders, summary.UrgentOpenOrders, summary.OnHoldOrders));
    }

    [Fact]
    public async Task Transports_show_basket_equipment_and_full_loads()
    {
        var transports = await _client.GetFromJsonAsync<List<TransportDto>>("/api/transports", Json);

        Assert.Equal(2, transports!.Count);
        Assert.All(transports, t => Assert.False(t.IsPartLoad));
        Assert.All(transports, t => Assert.True(t.FillRate >= 0.9m));
    }

    [Fact]
    public async Task Load_plan_groups_aluminium_baskets_and_excludes_held_material()
    {
        var plan = await _client.GetFromJsonAsync<LoadPlanDto>("/api/load-plan", Json);

        Assert.Equal(0.9m, plan!.FullLoadThreshold);
        Assert.Equal(5, plan.Proposals.Count);
        Assert.All(plan.Proposals, p => Assert.True(p.IsFull && p.FillRate >= 0.9m));
        Assert.Contains(plan.Proposals, p => p.OrderNumbers.Contains("TO-2026-0007"));
        Assert.DoesNotContain(plan.Proposals, p => p.OrderNumbers.Contains("TO-2026-0011"));   // on hold
    }

    [Fact]
    public async Task Genealogy_shows_billet_batches_and_loaded_baskets()
    {
        var tree = await _client.GetFromJsonAsync<GenealogyDto>("/api/heats/al-26031/genealogy", Json);

        Assert.Equal(("AL-26031", 8), (tree!.HeatNumber, tree.PieceCount));
        Assert.Equal(["AL-26031-B01", "AL-26031-B02"], tree.Pieces.Select(p => p.PieceNumber));
        Assert.Equal([4, 2], tree.Pieces.Select(p => p.Children.Count));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/heats/H-00000/genealogy")).StatusCode);
    }

    [Fact]
    public async Task Api_is_read_only()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new { orderNumber = "TO-2026-0099" });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Plc_endpoint_exposes_durable_envelope_and_policy_but_no_write_operation()
    {
        using var isolated = new ApiFactory();
        using var client = isolated.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/plc"));
        Assert.Equal(15, json.RootElement.GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("maxAttempts").GetInt32());
        var command = Assert.Single(json.RootElement.GetProperty("commands").EnumerateArray());
        Assert.Equal("Pending", command.GetProperty("status").GetString());
        Assert.Equal(64, command.GetProperty("payloadHash").GetString()!.Length);
        Assert.NotEqual(Guid.Empty, command.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/plc", new { })).StatusCode);
    }

    [Fact]
    public async Task Schedule_endpoint_exposes_next_action_and_quality_blocks_without_changing_plant()
    {
        using var isolated = new ApiFactory();
        using var client = isolated.CreateClient();
        var repository = isolated.Services.GetRequiredService<IOrderRepository>();
        var before = await repository.GetPlantStateAsync();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/schedule"));
        Assert.Equal("ArriveTransport", json.RootElement.GetProperty("next").GetProperty("kind").GetString());
        Assert.Contains(json.RootElement.GetProperty("blocked").EnumerateArray(),
            b => b.GetProperty("reference").GetString() == "AL-26052-K03" && b.GetProperty("reason").GetString() == "QualityHold");
        var after = await repository.GetPlantStateAsync();
        Assert.Equal(before.Baskets.Select(b => b.Version), after.Baskets.Select(b => b.Version));
        Assert.Equal(before.Orders.Count, after.Orders.Count);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/schedule", new { })).StatusCode);
    }

    [Fact]
    public async Task Labels_are_unavailable_before_approval_and_expose_preserved_package_traceability_afterwards()
    {
        using var isolated = new ApiFactory();
        using var client = isolated.CreateClient();
        var repository = isolated.Services.GetRequiredService<IOrderRepository>();
        var basket = (await repository.GetPlantStateAsync()).Baskets.Single(b => b.Number == "AL-26052-K01");
        await repository.StartPackingAsync(basket.Number, basket.Version, "test", DateTime.UtcNow);
        var package = Assert.Single((await repository.GetPlantStateAsync()).Packages);
        await repository.CompletePackingAsync(package.Number, package.Version, "test", DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/packages/{package.Number}/label")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/packages/UNKNOWN/label")).StatusCode);
        package = Assert.Single((await repository.GetPlantStateAsync()).Packages);
        await repository.InspectPackageAsync(package.Number, package.Version, true, true, "QA", "Checked", DateTime.UtcNow);
        using var response = await client.GetAsync($"/api/packages/{package.Number}/label");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains(package.MaterialLotNumber, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/plant"));
        var empty = json.RootElement.GetProperty("baskets").EnumerateArray().Single(b => b.GetProperty("basketNumber").GetString() == basket.Number);
        Assert.True(empty.GetProperty("isEmpty").GetBoolean());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("materialLotNumber").ValueKind);
        Assert.Equal(1, json.RootElement.GetProperty("packages").GetArrayLength());
    }

    [Fact]
    public async Task Plant_endpoint_exposes_basket_positions_and_reserved_slots()
    {
        using var response = await _client.GetAsync("/api/plant");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var stations = json.RootElement.GetProperty("stations").EnumerateArray().ToList();
        Assert.Equal(7, stations.Count);
        var packing = stations.Single(s => s.GetProperty("id").GetString() == "PACK-01");
        Assert.Equal(1, packing.GetProperty("reserved").GetInt32());
        Assert.Equal(0, packing.GetProperty("occupied").GetInt32());
        var baskets = json.RootElement.GetProperty("baskets").EnumerateArray().ToList();
        Assert.Equal(12, baskets.Count);
        var moving = Assert.Single(baskets, b => b.GetProperty("isMoving").GetBoolean());
        Assert.Equal(JsonValueKind.Null, moving.GetProperty("location").ValueKind);
        Assert.Equal("PACK-01", moving.GetProperty("reservedLocationId").GetString());
        Assert.False(string.IsNullOrEmpty(moving.GetProperty("activeTransportNumber").GetString()));
        Assert.Contains(json.RootElement.GetProperty("materialLots").EnumerateArray(),
            lot => lot.GetProperty("number").GetString() == moving.GetProperty("materialLotNumber").GetString());
        var equipment = json.RootElement.GetProperty("equipment").EnumerateArray().ToList();
        Assert.Equal(7, equipment.Count);
        Assert.Equal("Busy", equipment.Single(e => e.GetProperty("id").GetString() == "SHUTTLE-01").GetProperty("status").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("recipes").GetArrayLength());
        Assert.Equal(0, json.RootElement.GetProperty("ovenCycles").GetArrayLength());
    }
}
