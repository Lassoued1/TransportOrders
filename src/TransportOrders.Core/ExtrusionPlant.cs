namespace TransportOrders.Core;

public enum StationKind { Press, AgingOven, Packing, Shipping, Buffer }

public sealed record PlantStation(string Id, StationKind Kind, int Capacity);

/// <summary>A basket keeps its piece number across successive transport orders.</summary>
public sealed record BasketPosition(Basket Basket, MaterialLot MaterialLot, TransportOrder? Order)
{
    public string BasketNumber => Basket.Number;
    public bool IsMoving => Basket.IsMoving;
    public string? Location => Basket.LocationId;
    public bool IsShipped => !IsMoving && Location == ExtrusionPlant.Shipping;
}

/// <summary>Illustrative extrusion layout; capacities are demo values, not machine specifications.</summary>
public static class ExtrusionPlant
{
    public const string Shipping = "SHIP-01";
    public static IReadOnlyList<PlantStation> Stations { get; } = Array.AsReadOnly<PlantStation>(
    [
        new("PRESS-01", StationKind.Press, 6), new("PRESS-02", StationKind.Press, 6),
        new("OVEN-01", StationKind.AgingOven, 2), new("OVEN-02", StationKind.AgingOven, 2),
        new("PACK-01", StationKind.Packing, 1), new("PACK-02", StationKind.Packing, 1),
        new(Shipping, StationKind.Shipping, 12),
    ]);

    public static int Occupied(string station, IEnumerable<BasketPosition> baskets) =>
        baskets.Count(b => !b.IsMoving && b.Location == station);

    public static int Reserved(string station, IEnumerable<BasketPosition> baskets) =>
        baskets.Count(b => b.IsMoving && b.Basket.ReservedLocationId == station);

    public static string? NextDestination(BasketPosition basket, IReadOnlyList<BasketPosition> baskets, IReadOnlyList<PlantLocation> locations)
    {
        if (basket.Basket.IsEmpty || basket.IsMoving || basket.Order?.Status != OrderStatus.Done) return null;
        var kind = locations.FirstOrDefault(s => s.Id == basket.Location)?.Kind;
        if (kind != StationKind.AgingOven) return null;
        // Include waiting departures as well as reservations to balance the two packing queues.
        return locations.Where(s => s.Kind == StationKind.Packing)
            .OrderBy(s => Occupied(s.Id, baskets) + baskets.Count(b => b.Order?.IsOpen == true && b.Order.TargetLocation == s.Id))
            .ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault()?.Id;
    }
}
