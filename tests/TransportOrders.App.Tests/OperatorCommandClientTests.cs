using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public class OperatorCommandClientTests
{
    private sealed class Handler(HttpStatusCode? status) : HttpMessageHandler
    {
        public int Calls;
        public string? Payload;
        public string? Route;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Payload = await request.Content!.ReadAsStringAsync(cancellationToken); Route = request.RequestUri!.AbsolutePath;
            if (status is null) throw new HttpRequestException("Connection lost");
            return new(status.Value) { Content = JsonContent.Create(new OperatorCommandResult("server-result")) };
        }
    }
    private static Task Execute(IOperatorCommandClient client, string action, TransportOrder order) => action switch
    {
        "quality" => client.InspectAsync("PK-1", Guid.NewGuid(), false, true, "Damage"),
        "basket" => client.ReloadBasketAsync("B-1", Guid.NewGuid(), "LOT-1", 12, 1m, 6m, "OVEN-01"),
        "settings" => client.SetPlcSettingsAsync(Guid.NewGuid(), false, true, "Check channel"),
        "retry" => client.RetryPlcAsync(Guid.NewGuid(), Guid.NewGuid(), "Checked communication"),
        "arrival" => client.ConfirmPlcAsync(Guid.NewGuid(), Guid.NewGuid(), "Position checked"),
        "order" => client.ChangeOrderAsync(order, OrderStatus.OnHold, "Defect"),
        "dispatch" => client.DispatchAsync(new(Fleet.Extrusion[0], order.SourceLocation, order.TargetLocation, [order], false), PartLoadReason.EndOfShift),
        _ => client.ConfirmTransportAsync("TR-2026-0001", [order], "Position verified")
    };
    private static TransportOrder Order() => new("TO-2026-0001", "LOT-1", "LOT-1-B", null, "EN AW-6060", 10,
        1m, 6m, "PRESS-01", "OVEN-01", OrderPriority.Normal, DateTime.UtcNow.AddHours(2), DateTime.UtcNow.AddHours(-1), "seed");
    [Theory]
    [InlineData("quality")][InlineData("basket")][InlineData("settings")][InlineData("retry")]
    [InlineData("arrival")][InlineData("order")][InlineData("dispatch")][InlineData("transport")]
    public async Task Every_action_sends_its_concrete_payload_and_database_without_an_asserted_actor(string action)
    {
        var handler = new Handler(HttpStatusCode.OK); using var http = new HttpClient(handler);
        using var client = new OperatorCommandClient("C:\\plant\\extrusion.db", new Localizer(), http);
        var order = Order(); var revision = OrderRevision.For(order);
        await Execute(client, action, order);
        using var payload = JsonDocument.Parse(handler.Payload!);
        Assert.Equal("C:\\plant\\extrusion.db", payload.RootElement.GetProperty("expectedDatabasePath").GetString());
        Assert.True(payload.RootElement.EnumerateObject().Count() > 1);
        Assert.False(payload.RootElement.TryGetProperty("actor", out _));
        Assert.False(payload.RootElement.TryGetProperty("operator", out _));
        Assert.Equal(revision, OrderRevision.For(order)); // The UI entity is not mutated to simulate success.
        Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData(null)][InlineData(HttpStatusCode.Conflict)][InlineData(HttpStatusCode.Forbidden)]
    public async Task A_failed_order_command_does_not_mutate_the_local_snapshot_or_resend(HttpStatusCode? status)
    {
        var handler = new Handler(status); using var http = new HttpClient(handler);
        using var client = new OperatorCommandClient("C:\\plant\\extrusion.db", new Localizer(), http);
        var order = Order(); var revision = OrderRevision.For(order);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ChangeOrderAsync(order, OrderStatus.OnHold, "Defect"));
        Assert.Equal(1, handler.Calls); Assert.Equal(revision, OrderRevision.For(order)); Assert.Equal(OrderStatus.Planned, order.Status);
    }
}
