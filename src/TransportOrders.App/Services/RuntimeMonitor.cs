using System.Net.Http;
using System.Net.Http.Json;
using System.IO;

namespace TransportOrders.App.Services;

public sealed record RuntimeSnapshot(bool Enabled, bool Running, DateTime? LastTickUtc,
    string? LastError, string DatabasePath, bool Paused = false);

public sealed class RuntimeMonitor(string databasePath, HttpClient? client = null) : IDisposable
{
    private readonly HttpClient _client = client ?? new() { Timeout = TimeSpan.FromSeconds(1) };
    public async Task<(string State, string Detail)> ReadAsync()
    {
        try
        {
            var url = Environment.GetEnvironmentVariable("TRANSPORTORDERS_SERVICE_URL") ?? "http://localhost:5080";
            var status = await _client.GetFromJsonAsync<RuntimeSnapshot>(url.TrimEnd('/') + "/api/runtime");
            if (status is null) return ("Unavailable", "");
            if (!string.Equals(Path.GetFullPath(status.DatabasePath), Path.GetFullPath(databasePath), StringComparison.OrdinalIgnoreCase))
                return ("DifferentDatabase", "");
            if (!string.IsNullOrEmpty(status.LastError)) return ("Error", status.LastError);
            return (!status.Enabled || !status.Running ? "Stopped" : status.Paused ? "Paused" : "Running",
                status.LastTickUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        { return ("Unavailable", ""); }
    }
    public void Dispose() { if (client is null) _client.Dispose(); }
}
