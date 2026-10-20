using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransportOrders.Core;

namespace TransportOrders.App.Services;

public interface IOperatorCommandClient
{
    Task InspectAsync(string number, Guid version, bool visual, bool quantity, string reason);
    Task ReloadBasketAsync(string number, Guid version, string lot, int quantity, decimal weight, decimal length, string oven);
    Task SetPlcSettingsAsync(Guid version, bool connected, bool suppress, string reason);
    Task RetryPlcAsync(Guid id, Guid version, string reason);
    Task ConfirmPlcAsync(Guid id, Guid version, string reason);
    Task ChangeOrderAsync(TransportOrder order, OrderStatus target, string reason);
    Task<string> DispatchAsync(LoadProposal proposal, PartLoadReason? reason);
    Task ConfirmTransportAsync(string number, IReadOnlyList<TransportOrder> orders, string reason);
    Task<SimulationResult> StepAsync();
}

public sealed class OperatorCommandClient(string databasePath, ILocalizer localizer, HttpClient? client = null, PlantConnection? connection = null)
    : IOperatorCommandClient, IDisposable
{
    private readonly HttpClient _client = client ?? new(new HttpClientHandler { UseDefaultCredentials = true,
        AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
    private readonly string _database = connection is null ? Path.GetFullPath(databasePath) : "";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    private static string Segment(string value) => Uri.EscapeDataString(value);
    private async Task<T> Send<T>(string route, IPlantCommandRequest command)
    {
        var url = connection?.Address.AbsoluteUri ?? Environment.GetEnvironmentVariable("TRANSPORTORDERS_SERVICE_URL") ?? "http://localhost:5080";
        try
        {
            // Serialize the concrete request, not the marker interface.
            using var content = JsonContent.Create(command, command.GetType(), options: Json);
            using var response = await _client.PostAsync(url.TrimEnd('/') + route, content);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(localizer[response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "OperatorApi_Unauthorized",
                    HttpStatusCode.Conflict => "OperatorApi_Conflict",
                    HttpStatusCode.BadRequest => "OperatorApi_Invalid",
                    HttpStatusCode.NotFound => "OperatorApi_NotFound",
                    _ => "OperatorApi_Failed"
                }]);
            return await response.Content.ReadFromJsonAsync<T>(Json) ?? throw new JsonException("Missing command result.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { throw new InvalidOperationException(localizer["OperatorApi_Unavailable"], ex); }
    }
    public async Task InspectAsync(string number, Guid version, bool visual, bool quantity, string reason) =>
        _ = await Send<OperatorCommandResult>($"/api/packages/{Segment(number)}/inspection", new QualityCommand(version, visual, quantity, reason, _database, connection?.RequirePlantId()));
    public async Task ReloadBasketAsync(string number, Guid version, string lot, int quantity, decimal weight, decimal length, string oven) =>
        _ = await Send<OperatorCommandResult>($"/api/baskets/{Segment(number)}/reload", new BasketReloadCommand(version, lot, quantity, weight, length, oven, _database, connection?.RequirePlantId()));
    public async Task SetPlcSettingsAsync(Guid version, bool connected, bool suppress, string reason) =>
        _ = await Send<OperatorCommandResult>("/api/plc/simulation/settings", new PlcSettingsCommand(version, connected, suppress, reason, _database, connection?.RequirePlantId()));
    public async Task RetryPlcAsync(Guid id, Guid version, string reason) =>
        _ = await Send<OperatorCommandResult>($"/api/plc/commands/{id}/retry", new PlcOperatorCommand(version, reason, _database, connection?.RequirePlantId()));
    public async Task ConfirmPlcAsync(Guid id, Guid version, string reason) =>
        _ = await Send<OperatorCommandResult>($"/api/plc/commands/{id}/manual-arrival", new PlcOperatorCommand(version, reason, _database, connection?.RequirePlantId()));
    public async Task ChangeOrderAsync(TransportOrder order, OrderStatus target, string reason) =>
        _ = await Send<OperatorCommandResult>($"/api/orders/{Segment(order.OrderNumber)}/state", new OrderStateCommand(OrderRevision.For(order), target, reason, _database, connection?.RequirePlantId()));
    public async Task<string> DispatchAsync(LoadProposal proposal, PartLoadReason? reason) =>
        (await Send<OperatorCommandResult>("/api/transports/dispatch", new DispatchCommand(proposal.Orders.Select(OrderReference.From).ToArray(),
            proposal.Vehicle.Type, reason, _database, connection?.RequirePlantId()))).Reference;
    public async Task ConfirmTransportAsync(string number, IReadOnlyList<TransportOrder> orders, string reason) =>
        _ = await Send<OperatorCommandResult>($"/api/transports/{Segment(number)}/manual-arrival",
            new TransportArrivalCommand(orders.Select(OrderReference.From).ToArray(), reason, _database, connection?.RequirePlantId()));
    public Task<SimulationResult> StepAsync() => Send<SimulationResult>("/api/simulation/step", new DemoStepCommand(_database, connection?.RequirePlantId()));
    public void Dispose() { if (client is null) _client.Dispose(); }
}
