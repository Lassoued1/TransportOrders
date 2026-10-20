using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using TransportOrders.Core;

namespace TransportOrders.App.Services;

public sealed class OperatorAccessMonitor(string databasePath, HttpClient? client = null) : IDisposable
{
    private readonly HttpClient _client = client ?? new(new HttpClientHandler { UseDefaultCredentials = true,
        AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
    public async Task<OperatorAccessSnapshot?> ReadAsync()
    {
        try
        {
            var url = Environment.GetEnvironmentVariable("TRANSPORTORDERS_SERVICE_URL") ?? "http://localhost:5080";
            var access = await _client.GetFromJsonAsync<OperatorAccessSnapshot>(url.TrimEnd('/') + "/api/access");
            return access is not null && string.Equals(Path.GetFullPath(databasePath), access.DatabasePath, StringComparison.OrdinalIgnoreCase)
                ? access : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }
    public void Dispose() { if (client is null) _client.Dispose(); }
}
