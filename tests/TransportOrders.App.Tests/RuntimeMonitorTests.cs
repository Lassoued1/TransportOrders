using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using TransportOrders.App.Services;

namespace TransportOrders.App.Tests;

public class RuntimeMonitorTests
{
    private sealed class ReplyHandler(RuntimeSnapshot? snapshot) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (snapshot is null) throw new HttpRequestException("Offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(snapshot) });
        }
    }
    [Theory]
    [InlineData(true, true, null, "C:\\plant\\extrusion.db", "Running")]
    [InlineData(true, false, null, "C:\\plant\\extrusion.db", "Stopped")]
    [InlineData(false, false, null, "C:\\plant\\extrusion.db", "Stopped")]
    [InlineData(true, true, "PLC failure", "C:\\plant\\extrusion.db", "Error")]
    [InlineData(true, true, null, "C:\\other\\extrusion.db", "DifferentDatabase")]
    public async Task Monitor_distinguishes_runtime_state_and_wrong_database(bool enabled, bool running, string? error,
        string path, string expected)
    {
        using var client = new HttpClient(new ReplyHandler(new(enabled, running, DateTime.UtcNow, error, path)));
        using var monitor = new RuntimeMonitor("C:\\plant\\extrusion.db", client);
        Assert.Equal(expected, (await monitor.ReadAsync()).State);
    }
    [Fact]
    public async Task Offline_endpoint_is_unavailable_rather_than_asserted_stopped()
    {
        using var client = new HttpClient(new ReplyHandler(null));
        using var monitor = new RuntimeMonitor("C:\\plant\\extrusion.db", client);
        Assert.Equal("Unavailable", (await monitor.ReadAsync()).State);
    }
}
