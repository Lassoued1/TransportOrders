using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TransportOrders.Core;

namespace TransportOrders.IntegrationTests;

public class OperatorCommandApiTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly TempDatabase _database = new();
        public string Path => _database.Path;
        public Clock Clock { get; } = new();
        public bool GrantOperators { get; init; } = true;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.UseSetting("Database:Path", Path);
            if (GrantOperators) builder.UseSetting("OperatorCommands:AllowedOperators:0", "PLANT\\Operator");
            builder.UseSetting("EquipmentControl:AllowedOperators:0", "PLANT\\Operator");
            builder.UseTestWindowsAuthentication();
            builder.ConfigureTestServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock); });
        }
        public IOrderRepository Repository => Services.GetRequiredService<IOrderRepository>();
        public HttpClient Client(string? actor = "PLANT\\Operator")
        { var client = CreateClient(); if (actor is not null) client.DefaultRequestHeaders.Add("Test-User", actor); return client; }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); _database.Dispose(); }
    }
    [Theory]
    [InlineData("/api/packages/PK/inspection")]
    [InlineData("/api/baskets/B/reload")]
    [InlineData("/api/plc/simulation/settings")]
    [InlineData("/api/plc/commands/00000000-0000-0000-0000-000000000001/retry")]
    [InlineData("/api/plc/commands/00000000-0000-0000-0000-000000000001/manual-arrival")]
    [InlineData("/api/orders/TO-2026-0001/state")]
    [InlineData("/api/transports/dispatch")]
    [InlineData("/api/transports/TR/manual-arrival")]
    [InlineData("/api/simulation/step")]
    public async Task Every_operator_route_requires_an_authenticated_authorized_account(string route)
    {
        using var factory = new Factory(); using var anonymous = factory.Client(null); using var forbidden = factory.Client("PLANT\\Other");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(route, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await forbidden.PostAsJsonAsync(route, new { })).StatusCode);
    }
    [Fact]
    public async Task Equipment_permission_alone_does_not_grant_the_new_operator_permission()
    {
        using var factory = new Factory { GrantOperators = false }; using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/simulation/step", new DemoStepCommand(factory.Path))).StatusCode);
    }
    [Fact]
    public async Task Manual_simulation_step_is_executed_on_the_service()
    {
        using var factory = new Factory(); using var client = factory.Client();
        var response = await client.PostAsJsonAsync("/api/simulation/step", new DemoStepCommand(factory.Path));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PlcCommandStatus.Applied, Assert.Single((await factory.Repository.GetPlantStateAsync()).PlcCommands).Status);
    }
    [Fact]
    public async Task Order_hold_release_cancel_are_audited_and_stale_or_illegal_changes_are_rejected()
    {
        using var factory = new Factory(); using var client = factory.Client();
        var order = (await factory.Repository.GetAllAsync()).First(o => o.Status == OrderStatus.Planned);
        var held = new OrderStateCommand(OrderRevision.For(order), OrderStatus.OnHold, "Surface defect", factory.Path);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/orders/{order.OrderNumber}/state", held)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/orders/{order.OrderNumber}/state", held)).StatusCode);
        order = (await factory.Repository.GetByOrderNumberAsync(order.OrderNumber))!;
        Assert.Equal(OrderStatus.OnHold, order.Status); Assert.Equal("PLANT\\Operator", order.History.Last().ChangedBy);
        Assert.Equal("Surface defect", order.HoldReason);
        var release = new OrderStateCommand(OrderRevision.For(order), OrderStatus.Planned, "QA released", factory.Path);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/orders/{order.OrderNumber}/state", release)).StatusCode);
        order = (await factory.Repository.GetByOrderNumberAsync(order.OrderNumber))!;
        var invalid = new OrderStateCommand(OrderRevision.For(order), OrderStatus.Done, "Bypass transfer", factory.Path);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/orders/{order.OrderNumber}/state", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/orders/{order.OrderNumber}/state", invalid with { TargetStatus = OrderStatus.Cancelled })).StatusCode);
        Assert.Equal(OrderStatus.Cancelled, (await factory.Repository.GetByOrderNumberAsync(order.OrderNumber))!.Status);
    }
    [Fact]
    public async Task Concurrent_order_commands_from_same_revision_apply_once()
    {
        using var factory = new Factory(); using var first = factory.Client(); using var second = factory.Client();
        var order = (await factory.Repository.GetAllAsync()).First(o => o.Status == OrderStatus.Planned);
        var command = new OrderStateCommand(OrderRevision.For(order), OrderStatus.OnHold, "First station", factory.Path);
        var responses = await Task.WhenAll(first.PostAsJsonAsync($"/api/orders/{order.OrderNumber}/state", command),
            second.PostAsJsonAsync($"/api/orders/{order.OrderNumber}/state", command with { Reason = "Other station" }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(order.History.Count + 1, (await factory.Repository.GetByOrderNumberAsync(order.OrderNumber))!.History.Count);
    }
    private static async Task<PackedPackage> Package(Factory factory)
    {
        await new ExtrusionSimulator(factory.Repository, factory.Clock).StepAsync();
        var state = await factory.Repository.GetPlantStateAsync();
        var basket = state.Baskets.Single(b => b.LocationId == "PACK-01");
        await factory.Repository.StartPackingAsync(basket.Number, basket.Version, "setup", factory.Clock.Now.UtcDateTime);
        var package = (await factory.Repository.GetPlantStateAsync()).Packages.Single();
        await factory.Repository.CompletePackingAsync(package.Number, package.Version, "setup", factory.Clock.Now.UtcDateTime);
        return (await factory.Repository.GetPlantStateAsync()).Packages.Single();
    }
    [Fact]
    public async Task Quality_rejection_then_reinspection_preserves_basket_until_approval_and_stale_check_does_not_unload_twice()
    {
        using var factory = new Factory(); using var client = factory.Client(); var package = await Package(factory);
        var rejected = new QualityCommand(package.Version, false, true, "Packaging damaged", factory.Path);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/packages/{package.Number}/inspection", rejected)).StatusCode);
        var state = await factory.Repository.GetPlantStateAsync(); package = state.Packages.Single();
        Assert.Equal(PackageStatus.Rejected, package.Status); Assert.Null(package.LabelText);
        Assert.False(state.Baskets.Single(b => b.Number == package.SourceBasketNumber).IsEmpty);
        var approved = rejected with { ExpectedVersion = package.Version, VisualPassed = true, Reason = "Repacked and verified" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/packages/{package.Number}/inspection", approved)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/packages/{package.Number}/inspection", approved)).StatusCode);
        state = await factory.Repository.GetPlantStateAsync(); package = state.Packages.Single();
        Assert.Equal(PackageStatus.Ready, package.Status); Assert.NotNull(package.LabelText);
        Assert.True(state.Baskets.Single(b => b.Number == package.SourceBasketNumber).IsEmpty);
        Assert.Equal("PLANT\\Operator", package.History.Last().User);
    }
    [Fact]
    public async Task Basket_reload_creates_one_new_load_and_order_with_authenticated_actor()
    {
        using var factory = new Factory(); using var client = factory.Client();
        PlantState state = await factory.Repository.GetPlantStateAsync();
        var simulator = new ExtrusionSimulator(factory.Repository, factory.Clock);
        for (var step = 0; step < 100 && !state.Baskets.Any(b => b.IsEmpty && !b.IsMoving && b.LocationId?.StartsWith("PRESS") == true); step++)
        { await simulator.StepAsync(); state = await factory.Repository.GetPlantStateAsync(); }
        var basket = state.Baskets.First(b => b.IsEmpty && !b.IsMoving && b.LocationId!.StartsWith("PRESS"));
        var lot = state.MaterialLots.First(l => l.Material.StartsWith("EN AW-6060"));
        var command = new BasketReloadCommand(basket.Version, lot.Number, 15, 1.2m, 6.4m, "OVEN-01", factory.Path);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/baskets/{basket.Number}/reload", command)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/baskets/{basket.Number}/reload", command)).StatusCode);
        state = await factory.Repository.GetPlantStateAsync();
        var reloaded = state.Baskets.Single(b => b.Number == basket.Number);
        Assert.Equal(basket.ContentSequence + 1, reloaded.ContentSequence);
        Assert.Equal(15, reloaded.Quantity);
        var order = Assert.Single(state.Orders, o => o.PieceNumber == basket.Number && o.BasketContentSequence == reloaded.ContentSequence);
        Assert.Equal("PLANT\\Operator", order.History.Last().ChangedBy);
    }
    [Fact]
    public async Task Plc_settings_timeout_retry_and_manual_arrival_preserve_identity_and_reservation()
    {
        using var factory = new Factory(); using var client = factory.Client();
        var state = await factory.Repository.GetPlantStateAsync(); var settings = state.PlcSimulation!;
        var disableReplies = new PlcSettingsCommand(settings.Version, true, true, "Test reply loss", factory.Path);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/plc/simulation/settings", disableReplies)).StatusCode);
        var plc = state.PlcCommands.Single();
        var exchange = new PlcExchangeService(factory.Repository, ((IPlcRepository)factory.Repository).PlcChannel, factory.Clock);
        await exchange.ProcessAsync(plc.Id); factory.Clock.Now = factory.Clock.Now.AddSeconds(16); await exchange.ProcessAsync(plc.Id);
        plc = (await factory.Repository.GetPlantStateAsync()).PlcCommands.Single();
        Assert.Equal(PlcCommandStatus.TimedOut, plc.Status);
        var retry = new PlcOperatorCommand(plc.Version, "Connection inspected", factory.Path);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/plc/commands/{plc.Id}/retry", retry)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/plc/commands/{plc.Id}/retry", retry)).StatusCode);
        state = await factory.Repository.GetPlantStateAsync(); plc = state.PlcCommands.Single();
        Assert.Equal(PlcCommandStatus.Pending, plc.Status); Assert.Equal(1, plc.Attempts);
        Assert.Contains(state.Baskets, b => b.IsMoving);
        var confirm = new PlcOperatorCommand(plc.Version, "Position physically verified", factory.Path);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/plc/commands/{plc.Id}/manual-arrival", confirm)).StatusCode);
        state = await factory.Repository.GetPlantStateAsync(); plc = state.PlcCommands.Single();
        Assert.Equal(PlcCommandStatus.Applied, plc.Status); Assert.Equal("PLANT\\Operator", plc.AppliedBy);
        Assert.DoesNotContain(state.Baskets, b => b.IsMoving);
    }
    [Fact]
    public async Task Transport_arrival_requires_complete_fresh_membership_then_dispatch_uses_server_fleet_and_creates_plc_command()
    {
        using var factory = new Factory(); using var client = factory.Client();
        var state = await factory.Repository.GetPlantStateAsync(); var transport = state.Transports.Single(t => t.Status == TransportStatus.InTransit);
        var members = state.Orders.Where(o => o.TransportNumber == transport.TransportNumber).Select(OrderReference.From).ToArray();
        var arrival = new TransportArrivalCommand(members, "Physical position verified", factory.Path);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/transports/{transport.TransportNumber}/manual-arrival", arrival with { Orders = [] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/transports/{transport.TransportNumber}/manual-arrival", arrival)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/transports/{transport.TransportNumber}/manual-arrival", arrival)).StatusCode);
        state = await factory.Repository.GetPlantStateAsync();
        var order = state.Orders.First(o => o.Status == OrderStatus.Planned && o.SourceLocation.StartsWith("PRESS") && o.TargetLocation == "OVEN-02");
        var dispatch = new DispatchCommand([OrderReference.From(order)], VehicleType.BasketShuttle, PartLoadReason.NoMatchingMaterial, factory.Path);
        var response = await client.PostAsJsonAsync("/api/transports/dispatch", dispatch);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<OperatorCommandResult>();
        state = await factory.Repository.GetPlantStateAsync();
        Assert.Contains(state.PlcCommands, c => c.Operation == result!.Reference && c.Status == PlcCommandStatus.Pending);
        Assert.Equal("PLANT\\Operator", state.Transports.Single(t => t.TransportNumber == result!.Reference).DispatchedBy);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transports/dispatch", dispatch)).StatusCode);
    }
    [Theory]
    [InlineData("reason")]
    [InlineData("database")]
    [InlineData("revision")]
    public async Task Malformed_or_wrong_database_order_command_leaves_original_state(string field)
    {
        using var factory = new Factory(); using var client = factory.Client();
        var order = (await factory.Repository.GetAllAsync()).First(o => o.Status == OrderStatus.Planned);
        var command = new OrderStateCommand(OrderRevision.For(order), OrderStatus.OnHold, "Hold", factory.Path);
        command = field switch { "reason" => command with { Reason = " " }, "database" => command with { ExpectedDatabasePath = "other.db" }, _ => command with { ExpectedRevision = "" } };
        Assert.Equal(field == "database" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/orders/{order.OrderNumber}/state", command)).StatusCode);
        Assert.Equal(OrderRevision.For(order), OrderRevision.For((await factory.Repository.GetByOrderNumberAsync(order.OrderNumber))!));
    }
}
