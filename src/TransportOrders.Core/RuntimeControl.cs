namespace TransportOrders.Core;

public sealed class RuntimeControlState
{
    public const string InitialVersion = "a721ab3c-5384-48c6-b00e-a6db4a859d2e";
    public int Id { get; set; } = 1;
    public bool Paused { get; set; }
    public Guid Version { get; set; } = Guid.Parse(InitialVersion);
}

public sealed class RuntimeControlEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Paused { get; set; }
    public Guid Version { get; set; }
    public DateTime AtUtc { get; set; }
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed record RuntimeControlCommand(Guid ExpectedVersion, bool Paused, string Reason,
    string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;

public interface IRuntimeControlRepository
{
    Task<RuntimeControlState> ReadAsync(CancellationToken ct = default);
    Task<RuntimeControlState> ChangeAsync(RuntimeControlCommand command, string actor, DateTime now, CancellationToken ct = default);
    Task<IReadOnlyList<RuntimeControlEvent>> HistoryAsync(CancellationToken ct = default);
}
