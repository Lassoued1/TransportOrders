using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using TransportOrders.Core;

namespace TransportOrders.App.Services;

public interface IEquipmentControlClient
{
    Task SetControlAsync(string id, Guid version, EquipmentMode mode, EquipmentCondition condition, string reason);
}

public sealed class EquipmentControlClient(string databasePath, ILocalizer localizer, HttpClient? client = null, PlantConnection? connection = null)
    : IEquipmentControlClient, IDisposable
{
    private readonly HttpClient _client = client ?? new(new HttpClientHandler { UseDefaultCredentials = true,
        AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };

    public async Task SetControlAsync(string id, Guid version, EquipmentMode mode, EquipmentCondition condition, string reason)
    {
        var url = connection?.Address.AbsoluteUri ?? Environment.GetEnvironmentVariable("TRANSPORTORDERS_SERVICE_URL") ?? "http://localhost:5080";
        try
        {
            using var response = await _client.PostAsJsonAsync(url.TrimEnd('/') + "/api/equipment/" + Uri.EscapeDataString(id) + "/control",
                new { ExpectedVersion = version, Mode = mode, Condition = condition, Reason = reason,
                    ExpectedDatabasePath = connection is null ? Path.GetFullPath(databasePath) : "", ExpectedPlantId = connection?.RequirePlantId() });
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(localizer[response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "ControlApi_Unauthorized",
                    HttpStatusCode.Conflict => "ControlApi_Conflict",
                    HttpStatusCode.BadRequest => "ControlApi_Invalid",
                    HttpStatusCode.NotFound => "ControlApi_NotFound",
                    _ => "ControlApi_Failed"
                }]);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A lost reply can follow a committed action. No automatic retry or SQLite fallback.
            throw new InvalidOperationException(localizer["ControlApi_Unavailable"], ex);
        }
    }
    public void Dispose() { if (client is null) _client.Dispose(); }
}
