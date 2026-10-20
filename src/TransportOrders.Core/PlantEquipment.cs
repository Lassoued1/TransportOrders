namespace TransportOrders.Core;

public enum EquipmentKind { Press, Oven, Shuttle, Packing }
public enum EquipmentMode { Automatic, Manual }
public enum EquipmentCondition { Operational, Fault, Maintenance }
public enum EquipmentStatus { Available, Busy, Fault, Maintenance }

public sealed partial class EquipmentEvent
{
    private EquipmentEvent() { }
    public EquipmentEvent(DateTime at, string by, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        At = at; By = by; Description = description;
    }
    public int Id { get; private set; }
    public DateTime At { get; private set; }
    public string By { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
}

public sealed partial class PlantEquipment
{
    public const string SharedShuttle = "SHUTTLE-01";
    private readonly List<EquipmentEvent> _history = [];
    private PlantEquipment() { }
    public PlantEquipment(string id, EquipmentKind kind, string? locationId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Id = id; Kind = kind; LocationId = locationId;
        Record(now, "INITIALIZER", "Registered");
    }
    public string Id { get; private set; } = string.Empty;
    public EquipmentKind Kind { get; private set; }
    public string? LocationId { get; private set; }
    public EquipmentMode Mode { get; private set; } = EquipmentMode.Automatic;
    public EquipmentCondition Condition { get; private set; } = EquipmentCondition.Operational;
    public string? ActiveOperation { get; private set; }
    public Guid Version { get; private set; }
    public IReadOnlyList<EquipmentEvent> History => _history.AsReadOnly();
    public EquipmentStatus Status => Condition switch
    {
        EquipmentCondition.Fault => EquipmentStatus.Fault,
        EquipmentCondition.Maintenance => EquipmentStatus.Maintenance,
        _ => ActiveOperation is null ? EquipmentStatus.Available : EquipmentStatus.Busy,
    };
    public bool CanOperate => Condition == EquipmentCondition.Operational;
    public bool CanSimulate => CanOperate && Mode == EquipmentMode.Automatic;
    public void SetControl(EquipmentMode mode, EquipmentCondition condition, string by, string reason, DateTime now)
    {
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(condition)) throw new ArgumentOutOfRangeException(nameof(condition));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        Mode = mode; Condition = condition;
        Record(now, by, $"{mode} / {condition}: {reason}");
    }
    public void Start(string operation, string by, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (!CanOperate || ActiveOperation is not null) throw new TransportRuleException($"Equipment {Id} is unavailable.");
        ActiveOperation = operation;
        Record(now, by, $"Started {operation}");
    }
    public void Finish(string operation, string by, DateTime now)
    {
        if (!CanOperate || ActiveOperation != operation) throw new TransportRuleException($"Equipment {Id} cannot acknowledge {operation}.");
        ActiveOperation = null;
        Record(now, by, $"Completed {operation}");
    }
    public void RecordCycleProgress(OvenCycle cycle, string by, DateTime now)
    {
        if (cycle.EquipmentId != Id || ActiveOperation != cycle.Id.ToString() || !CanSimulate)
            throw new TransportRuleException("The cycle does not match this active automatic oven.");
        Record(now, by, $"{cycle.Phase}: {cycle.ElapsedMinutes}/{cycle.TotalMinutes} simulated min");
    }
    private void Record(DateTime now, string by, string description)
    {
        _history.Add(new(now, by, description));
        Version = Guid.NewGuid();
    }
}
