namespace TransportOrders.Core;

public enum OvenPhase { Heating, Holding, Cooling, Completed }

public sealed partial class OvenCycleLoad
{
    private OvenCycleLoad() { }
    public OvenCycleLoad(Basket basket) { BasketNumber = basket.Number; MaterialLotNumber = basket.MaterialLotNumber; ContentSequence = basket.ContentSequence; }
    public Guid CycleId { get; private set; }
    public string BasketNumber { get; private set; } = string.Empty;
    public string MaterialLotNumber { get; private set; } = string.Empty;
    public int ContentSequence { get; private set; } = 1;
}

/// <summary>Durations and setpoint are copied at start so subsequent recipe edits cannot alter an active cycle.</summary>
public sealed partial class OvenCycle
{
    private readonly List<OvenCycleLoad> _load = [];
    private OvenCycle() { }
    public OvenCycle(string equipmentId, OvenRecipe recipe, IReadOnlyCollection<Basket> baskets,
        IReadOnlyCollection<MaterialLot> lots, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentId);
        if (baskets.Count == 0 || baskets.Select(b => b.Number).Distinct().Count() != baskets.Count)
            throw new TransportRuleException("A cycle requires a nonempty, distinct basket load.");
        if (baskets.Any(b => b.IsEmpty || b.IsMoving || !lots.Any(l => l.Number == b.MaterialLotNumber && recipe.Supports(l))))
            throw new TransportRuleException("Every basket must be stationary and compatible with the recipe.");
        Id = Guid.NewGuid(); EquipmentId = equipmentId; RecipeId = recipe.Id; RecipeRevision = recipe.Revision;
        TargetTemperature = recipe.TargetTemperature;
        HeatingMinutes = recipe.HeatingMinutes; HoldingMinutes = recipe.HoldingMinutes; CoolingMinutes = recipe.CoolingMinutes;
        StartedAt = now; UpdatedAt = now; Version = Guid.NewGuid();
        _load.AddRange(baskets.Select(b => new OvenCycleLoad(b)));
    }
    public Guid Id { get; private set; }
    public string EquipmentId { get; private set; } = string.Empty;
    public string RecipeId { get; private set; } = string.Empty;
    public int RecipeRevision { get; private set; } = 1;
    public decimal TargetTemperature { get; private set; }
    public int HeatingMinutes { get; private set; }
    public int HoldingMinutes { get; private set; }
    public int CoolingMinutes { get; private set; }
    public int ElapsedMinutes { get; private set; }
    public OvenPhase Phase { get; private set; } = OvenPhase.Heating;
    public DateTime StartedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public Guid Version { get; private set; }
    public IReadOnlyList<OvenCycleLoad> Load => _load.AsReadOnly();
    public int TotalMinutes => checked(HeatingMinutes + HoldingMinutes + CoolingMinutes);
    public decimal SimulatedTemperature => Phase switch
    {
        OvenPhase.Heating => 20m + (TargetTemperature - 20m) * ElapsedMinutes / HeatingMinutes,
        OvenPhase.Holding => TargetTemperature,
        OvenPhase.Cooling => TargetTemperature - (TargetTemperature - 20m) * (ElapsedMinutes - HeatingMinutes - HoldingMinutes) / CoolingMinutes,
        _ => 20m,
    };
    public void Advance(int minutes, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minutes);
        if (Phase == OvenPhase.Completed) throw new TransportRuleException("This oven cycle is already complete.");
        ElapsedMinutes = (int)Math.Min(TotalMinutes, (long)ElapsedMinutes + minutes);
        Phase = ElapsedMinutes < HeatingMinutes ? OvenPhase.Heating
            : ElapsedMinutes < HeatingMinutes + HoldingMinutes ? OvenPhase.Holding
            : ElapsedMinutes < TotalMinutes ? OvenPhase.Cooling : OvenPhase.Completed;
        UpdatedAt = now;
        if (Phase == OvenPhase.Completed) CompletedAt = now;
        Version = Guid.NewGuid();
    }
}
