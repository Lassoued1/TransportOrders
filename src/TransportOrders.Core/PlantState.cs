namespace TransportOrders.Core;

/// <summary>Consistent snapshot; order history is joined for display, never to infer physical position.</summary>
public sealed record PlantState(
    IReadOnlyList<Basket> Baskets, IReadOnlyList<MaterialLot> MaterialLots,
    IReadOnlyList<PlantLocation> Locations, IReadOnlyList<TransportOrder> Orders,
    IReadOnlyList<Transport> Transports)
{
    public IReadOnlyList<PlantEquipment> Equipment { get; init; } = [];
    public IReadOnlyList<OvenRecipe> Recipes { get; init; } = [];
    public IReadOnlyList<OvenCycle> Cycles { get; init; } = [];
    public IReadOnlyList<PackedPackage> Packages { get; init; } = [];
    public IReadOnlyList<BasketReturn> Returns { get; init; } = [];
    public IReadOnlyList<PlcCommand> PlcCommands { get; init; } = [];
    public PlcSimulationSettings? PlcSimulation { get; init; }
    public bool HasCompletedTreatment(Basket basket, string ovenId) => Cycles.Any(c => c.EquipmentId == ovenId
        && !basket.IsEmpty && c.Phase == OvenPhase.Completed && c.Load.Any(l => l.BasketNumber == basket.Number
            && l.MaterialLotNumber == basket.MaterialLotNumber && l.ContentSequence == basket.ContentSequence));

    public int Occupied(string location) => Baskets.Count(b => b.LocationId == location)
        + Packages.Count(p => p.LocationId == location && !Baskets.Any(b => b.Number == p.SourceBasketNumber && b.LocationId == location));
    public int Reserved(string location) => Baskets.Count(b => b.ReservedLocationId == location) + Packages.Count(p => p.ReservedLocationId == location);

    public bool CanTransfer(BasketPosition basket, bool automatic)
    {
        if (basket.Basket.IsEmpty || basket.Order is not { } order) return false;
        var involved = Equipment.Where(e => e.Id == PlantEquipment.SharedShuttle
            || e.LocationId == order.SourceLocation || e.LocationId == order.TargetLocation).ToList();
        if (involved.Any(e => !e.CanOperate || e.ActiveOperation is not null || (automatic && !e.CanSimulate))) return false;
        var source = Locations.FirstOrDefault(l => l.Id == order.SourceLocation);
        if (source?.Kind == StationKind.Packing) return false;
        if (source?.Kind == StationKind.AgingOven && !HasCompletedTreatment(basket.Basket, source.Id)) return false;
        if (Locations.FirstOrDefault(l => l.Id == order.TargetLocation)?.Kind == StationKind.AgingOven)
        {
            var load = Baskets.Where(b => b.LocationId == order.TargetLocation || b.ReservedLocationId == order.TargetLocation)
                .Append(basket.Basket).DistinctBy(b => b.Number).ToList();
            if (!Recipes.Any(r => load.All(b => MaterialLots.Any(l => l.Number == b.MaterialLotNumber && r.Supports(l))))) return false;
            // A finished load must be emptied before the next batch is admitted.
            if (load.Any(b => b.Number != basket.BasketNumber && HasCompletedTreatment(b, order.TargetLocation))) return false;
        }
        return true;
    }
    public IReadOnlyList<BasketPosition> Locate()
    {
        var latest = Orders.GroupBy(o => (o.PieceNumber, o.BasketContentSequence)).ToDictionary(g => g.Key,
            g => g.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.OrderNumber, StringComparer.Ordinal).First());
        var lots = MaterialLots.ToDictionary(l => l.Number);
        return Baskets.OrderBy(b => b.Number, StringComparer.Ordinal)
            .Select(b => new BasketPosition(b, lots[b.MaterialLotNumber], latest.GetValueOrDefault((b.Number, b.ContentSequence)))).ToList();
    }
    public bool HasRoom(string locationId, IReadOnlyList<BasketPosition> positions) =>
        Locations.FirstOrDefault(s => s.Id == locationId) is { } location
        && Occupied(locationId) + Reserved(locationId) < location.Capacity;
}
