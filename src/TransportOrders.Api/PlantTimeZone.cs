namespace TransportOrders.Api;

/// <summary>Time zone of the plant, used to compute shift boundaries (Plant:TimeZone, default: server time zone).</summary>
public sealed record PlantTimeZone(TimeZoneInfo Zone)
{
    public static PlantTimeZone FromConfiguration(IConfiguration configuration)
    {
        var id = configuration["Plant:TimeZone"];
        return new PlantTimeZone(string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(id));
    }
}
