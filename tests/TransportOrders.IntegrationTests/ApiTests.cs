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
        Assert.All(orders, o => Assert.Equal("pcs", o.Unit));
    }

    [Theory]
    [InlineData("Planned", 4)]
    [InlineData("intransit", 3)]
    [InlineData("Done", 3)]
    [InlineData("Cancelled", 2)]
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
        Assert.Contains(orders!, o => o.Material.StartsWith("Billet"));
        Assert.Contains(orders!, o => o.Material.StartsWith("Profile"));
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
        Assert.Equal(7, summary.OpenOrders);
        Assert.Equal(2, summary.OverdueOrders);
        Assert.Equal(2, summary.UrgentOpenOrders);
    }

    [Fact]
    public async Task Api_is_read_only()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new { orderNumber = "TO-2026-0099" });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
