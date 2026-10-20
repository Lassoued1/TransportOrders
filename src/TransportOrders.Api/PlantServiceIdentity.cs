using System.Security.Cryptography;
using System.Text;

namespace TransportOrders.Api;

public sealed class PlantServiceIdentity(IConfiguration configuration, string databasePath)
{
    public string Id { get; } = configuration["Plant:Id"] ?? Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Path.GetFullPath(databasePath).ToUpperInvariant()}")));
    public string Name { get; } = configuration["Plant:Name"] ?? "Aluminium extrusion demo";
    public bool Matches(string? plantId, string? expectedDatabase) => !string.IsNullOrWhiteSpace(plantId)
        ? string.Equals(plantId, Id, StringComparison.Ordinal)
        : string.Equals(expectedDatabase, databasePath, StringComparison.OrdinalIgnoreCase);
}
