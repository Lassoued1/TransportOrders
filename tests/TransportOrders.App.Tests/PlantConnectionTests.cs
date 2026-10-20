using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

public class PlantConnectionTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public string Identity = "plant-one";
        public int Contract = 1;
        public bool Offline;
        public string? Payload;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("Offline");
            if (request.Method == HttpMethod.Post)
            {
                Payload = await request.Content!.ReadAsStringAsync(ct);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new OperatorCommandResult("done")) };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PlantClientSnapshot(Contract, Identity,
                "Plant", "UTC", DateTime.UtcNow, PlantStateSnapshot.From(new([], [], [], [], [])),
                new("PLANT\\Operator", ["Supervisor"], PlantPermissions.All, ""), new(true, true, DateTime.UtcNow, null))) };
        }
    }
    [Theory]
    [InlineData("http://plant:5080")][InlineData("ftp://localhost")]
    [InlineData("https://user:pass@plant")][InlineData("https://plant/?token=x")]
    [InlineData("https://plant/path")][InlineData("https://plant/#secret")]
    public void Invalid_or_plaintext_remote_addresses_are_rejected(string address) =>
        Assert.Throws<ArgumentException>(() => new PlantConnection(address));

    [Fact]
    public async Task Commands_use_pinned_server_identity_without_a_database_path_and_fail_closed_when_offline()
    {
        var handler = new Handler(); using var http = new HttpClient(handler);
        using var connection = new PlantConnection("https://plant", client: http);
        var repository = new RemotePlantRepository(connection);
        using var commands = new OperatorCommandClient("", new Localizer(), http, connection);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.RetryPlcAsync(Guid.NewGuid(), Guid.NewGuid(), "Check"));
        await repository.GetPlantStateAsync();
        await commands.RetryPlcAsync(Guid.NewGuid(), Guid.NewGuid(), "Check");
        using var json = JsonDocument.Parse(handler.Payload!);
        Assert.Equal("plant-one", json.RootElement.GetProperty("expectedPlantId").GetString());
        Assert.Equal("", json.RootElement.GetProperty("expectedDatabasePath").GetString());
        await Assert.ThrowsAsync<NotSupportedException>(() => repository.NextTransportNumberAsync(DateTime.UtcNow));
        handler.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.GetPlantStateAsync());
        Assert.Null(connection.Snapshot);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.RetryPlcAsync(Guid.NewGuid(), Guid.NewGuid(), "Check"));
    }
    [Fact]
    public async Task Changed_installation_or_contract_disables_connection()
    {
        var handler = new Handler(); using var http = new HttpClient(handler);
        using var connection = new PlantConnection("https://plant", client: http);
        await connection.ReadAsync();
        handler.Identity = "other-plant";
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReadAsync());
        Assert.Null(connection.Snapshot);
        handler.Identity = "plant-one"; handler.Contract = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ReadAsync());
        Assert.Null(connection.Snapshot);
    }
}
