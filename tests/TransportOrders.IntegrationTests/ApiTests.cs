using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TransportOrders.Api;
using TransportOrders.Core;

namespace TransportOrders.IntegrationTests;

/// <summary>Runs the real API in memory against its own temporary SQLite file.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TempDatabase _database = new();

    public string DatabasePath => _database.Path;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Path", _database.Path);
        builder.UseSetting("Plant:TimeZone", "UTC");
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
        var orders = await _client.GetFromJsonAsync<List<OrderDto>>("/api/orders?heat=h-26031", Json);

        Assert.NotEmpty(orders!);
        Assert.All(orders!, o => Assert.Equal("H-26031", o.HeatNumber));
        Assert.Contains(orders!, o => o.Material == "1.2379 Rohblock");
        Assert.Contains(orders!, o => o.Material == "1.2379 Schmiedestab");
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
    public async Task Transports_show_fill_rate_and_recorded_part_load_reasons()
    {
        var transports = await _client.GetFromJsonAsync<List<TransportDto>>("/api/transports", Json);

        Assert.Equal(2, transports!.Count);
        Assert.All(transports, t => Assert.True(t.IsPartLoad && t.PartLoadReason is not null));
        var heavy = transports.Single(t => t.PartLoadReason == PartLoadReason.PayloadLimit);
        Assert.True(heavy.LoadedWeightTonnes >= heavy.PayloadTonnes * 0.9m);
    }

    [Fact]
    public async Task Load_plan_proposes_full_loads_and_one_urgent_part_load()
    {
        var plan = await _client.GetFromJsonAsync<LoadPlanDto>("/api/load-plan", Json);

        Assert.Equal(0.9m, plan!.FullLoadThreshold);
        Assert.Equal(3, plan.Proposals.Count);
        Assert.Equal(2, plan.Proposals.Count(p => p.IsFull && p.FillRate >= 0.9m));
        var partLoad = plan.Proposals.Single(p => !p.IsFull);
        Assert.Equal(["TO-2026-0007"], partLoad.OrderNumbers);
        Assert.Equal(PartLoadReason.UrgentMaterial, partLoad.SuggestedReason);
        Assert.DoesNotContain(plan.Proposals, p => p.OrderNumbers.Contains("TO-2026-0011"));   // on hold
    }

    [Fact]
    public async Task Genealogy_shows_ingots_and_the_bars_made_from_them()
    {
        var tree = await _client.GetFromJsonAsync<GenealogyDto>("/api/heats/h-26031/genealogy", Json);

        Assert.Equal(("H-26031", 6), (tree!.HeatNumber, tree.PieceCount));
        Assert.Equal(["H-26031-1", "H-26031-2"], tree.Pieces.Select(p => p.PieceNumber));
        Assert.All(tree.Pieces, ingot => Assert.Equal(2, ingot.Children.Count));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/heats/H-00000/genealogy")).StatusCode);
    }

    [Fact]
    public async Task Api_is_read_only()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new { orderNumber = "TO-2026-0099" });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
