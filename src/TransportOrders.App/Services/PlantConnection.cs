using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransportOrders.Core;

namespace TransportOrders.App.Services;

/// <summary>One immutable endpoint and a pinned installation identity for reads and commands.</summary>
public sealed class PlantConnection : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    public Uri Address { get; }
    public string? PlantId { get; private set; }
    public PlantClientSnapshot? Snapshot { get; private set; }
    public event Action<PlantClientSnapshot?>? Changed;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public PlantConnection(string address, string? plantId = null, HttpClient? client = null)
    {
        Address = ValidateAddress(address);
        PlantId = string.IsNullOrWhiteSpace(plantId) ? null : plantId;
        _ownsClient = client is null;
        _client = client ?? new(new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(5) };
    }
    public static Uri ValidateAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath != "/" || (uri.Scheme == "http" && !uri.IsLoopback))
            throw new ArgumentException("Use an HTTPS server address, or HTTP on localhost only.", nameof(address));
        return uri;
    }
    public async Task<PlantState> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _client.GetAsync(new Uri(Address, "api/client-state"), ct);
            response.EnsureSuccessStatusCode();
            var snapshot = await response.Content.ReadFromJsonAsync<PlantClientSnapshot>(Json, ct)
                ?? throw new JsonException("Missing plant snapshot.");
            if (snapshot.ContractVersion != 1 || string.IsNullOrWhiteSpace(snapshot.PlantId))
                throw new InvalidOperationException("Unsupported plant snapshot contract.");
            if (PlantId is not null && !string.Equals(PlantId, snapshot.PlantId, StringComparison.Ordinal))
                throw new InvalidOperationException("The server belongs to a different plant installation.");
            var state = snapshot.State.Restore();
            PlantId = snapshot.PlantId;
            Snapshot = snapshot;
            Changed?.Invoke(snapshot);
            return state;
        }
        catch
        {
            Snapshot = null;
            Changed?.Invoke(null);
            throw;
        }
    }
    public string RequirePlantId() => Snapshot is not null && PlantId is not null ? PlantId
        : throw new InvalidOperationException("Refresh the central service connection before issuing commands.");
    public async Task AcknowledgeAlarmAsync(Guid id, Guid version, string reason)
    {
        using var response = await _client.PostAsJsonAsync(new Uri(Address, $"api/alarms/{id}/acknowledge"),
            new { ExpectedVersion = version, Reason = reason, ExpectedPlantId = RequirePlantId() });
        response.EnsureSuccessStatusCode();
    }
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
    public async Task ChangeRecipeAsync(string id, string action, Guid version, RecipeParameters? parameters, string reason)
    {
        using var response = await _client.PostAsJsonAsync(new Uri(Address, $"api/recipes/{Uri.EscapeDataString(id)}/{action}"),
            new { ExpectedVersion = version, Parameters = parameters, Reason = reason, ExpectedPlantId = RequirePlantId() });
        response.EnsureSuccessStatusCode();
    }
}
