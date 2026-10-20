namespace TransportOrders.Core;

public enum AlarmCode { EquipmentFault, PlcOffline, PlcTimedOut, PlcRejected, RuntimeError, BackupError }
public sealed record AlarmCondition(string Key, AlarmCode Code, string Source, string Detail);
public sealed record AlarmEventSnapshot(DateTime AtUtc, string Actor, string Action, string Note);
public sealed record PlantAlarmSnapshot(Guid Id, Guid Version, AlarmCode Code, string Source, string Detail,
    bool IsActive, DateTime RaisedUtc, DateTime? ClearedUtc, DateTime? AcknowledgedUtc, string? AcknowledgedBy,
    IReadOnlyList<AlarmEventSnapshot> History)
{
    public static PlantAlarmSnapshot From(PlantAlarm alarm) => new(alarm.Id, alarm.Version, alarm.Code, alarm.Source,
        alarm.Detail, alarm.IsActive, alarm.RaisedUtc, alarm.ClearedUtc, alarm.AcknowledgedUtc, alarm.AcknowledgedBy,
        alarm.History.Select(e => new AlarmEventSnapshot(e.AtUtc, e.Actor, e.Action, e.Note)).ToArray());
}
public sealed class AlarmEvent
{
    private AlarmEvent() { }
    public AlarmEvent(DateTime now, string actor, string action, string note)
    { AtUtc = now; Actor = actor; Action = action; Note = note; }
    public int Id { get; private set; }
    public DateTime AtUtc { get; private set; }
    public string Actor { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string Note { get; private set; } = "";
}
public sealed class PlantAlarm
{
    private readonly List<AlarmEvent> _history = [];
    private PlantAlarm() { }
    public PlantAlarm(AlarmCondition condition, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition.Key); ArgumentException.ThrowIfNullOrWhiteSpace(condition.Source);
        if (!Enum.IsDefined(condition.Code)) throw new ArgumentOutOfRangeException(nameof(condition));
        Id = Guid.NewGuid(); Key = condition.Key; Code = condition.Code; Source = condition.Source;
        Detail = condition.Detail; IsActive = true; RaisedUtc = now; Record(now, "ALARM-MONITOR", "Raised", Detail);
    }
    public Guid Id { get; private set; }
    public Guid Version { get; private set; }
    public string Key { get; private set; } = "";
    public AlarmCode Code { get; private set; }
    public string Source { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public bool IsActive { get; private set; }
    public DateTime RaisedUtc { get; private set; }
    public DateTime? ClearedUtc { get; private set; }
    public DateTime? AcknowledgedUtc { get; private set; }
    public string? AcknowledgedBy { get; private set; }
    public IReadOnlyList<AlarmEvent> History => _history.AsReadOnly();
    public void UpdateDetail(string detail, DateTime now)
    {
        if (Detail == detail) return;
        Detail = detail; Record(now, "ALARM-MONITOR", "Updated", detail);
    }
    public void Clear(DateTime now)
    {
        if (!IsActive) return;
        IsActive = false; ClearedUtc = now; Record(now, "ALARM-MONITOR", "Cleared", "Condition no longer present");
    }
    public void Acknowledge(Guid expectedVersion, string actor, string reason, DateTime now)
    {
        if (Version != expectedVersion) throw new TransportRuleException("Alarm changed; refresh before acknowledging.");
        if (AcknowledgedUtc is not null) throw new TransportRuleException("Alarm already acknowledged.");
        ArgumentException.ThrowIfNullOrWhiteSpace(actor); ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 1000) throw new ArgumentException("Reason must not exceed 1000 characters.");
        AcknowledgedUtc = now; AcknowledgedBy = actor; Record(now, actor, "Acknowledged", reason);
    }
    private void Record(DateTime now, string actor, string action, string note)
    { _history.Add(new(now, actor, action, note)); Version = Guid.NewGuid(); }
}
public interface IAlarmRepository
{
    Task SynchronizeAsync(IReadOnlyList<AlarmCondition> conditions, DateTime now, CancellationToken ct = default);
    Task<IReadOnlyList<PlantAlarmSnapshot>> ReadAsync(CancellationToken ct = default);
    Task AcknowledgeAsync(Guid id, Guid version, string actor, string reason, DateTime now, CancellationToken ct = default);
}
public static class AlarmDetection
{
    public static IReadOnlyList<AlarmCondition> Detect(PlantState state, string? runtimeError, string? backupError)
    {
        var conditions = state.Equipment.Where(e => e.Condition == EquipmentCondition.Fault)
            .Select(e => new AlarmCondition($"equipment:{e.Id}", AlarmCode.EquipmentFault, e.Id, "")).ToList();
        if (state.PlcSimulation is { Connected: false }) conditions.Add(new("plc:offline", AlarmCode.PlcOffline, "PLC-SIM", ""));
        foreach (var c in state.PlcCommands.Where(c => c.Status is PlcCommandStatus.TimedOut or PlcCommandStatus.Rejected))
            conditions.Add(new($"plc:{c.Id}:{c.Status}", c.Status == PlcCommandStatus.TimedOut ? AlarmCode.PlcTimedOut : AlarmCode.PlcRejected,
                c.Operation, c.LastError ?? ""));
        if (!string.IsNullOrEmpty(runtimeError)) conditions.Add(new("runtime:error", AlarmCode.RuntimeError, "RUNTIME", runtimeError));
        if (!string.IsNullOrEmpty(backupError)) conditions.Add(new("backup:error", AlarmCode.BackupError, "BACKUP", backupError));
        return conditions;
    }
}
