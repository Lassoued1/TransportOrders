namespace TransportOrders.Core;

public sealed partial class PlantLocation
{
    private PlantLocation() { }
    public PlantLocation(string id, StationKind kind, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Id = id.Trim();
        Kind = kind;
        Capacity = capacity;
    }
    public string Id { get; private set; } = string.Empty;
    public StationKind Kind { get; private set; }
    public int Capacity { get; private set; }
}
