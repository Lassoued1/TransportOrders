namespace TransportOrders.Core;

public sealed partial class PlcSimulationSettings
{
    public int Id { get; private set; } = 1;
    public bool Connected { get; private set; } = true;
    public bool SuppressAcknowledgements { get; private set; }
    public Guid Version { get; private set; } = Guid.NewGuid();
    public string ChangedBy { get; private set; } = "INITIALIZER";
    public string Reason { get; private set; } = "Demo channel initialized";
    public DateTime ChangedAt { get; private set; }
    public void Set(bool connected, bool suppressAcknowledgements, string user, string reason, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user); ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Connected = connected; SuppressAcknowledgements = suppressAcknowledgements; ChangedBy = user; Reason = reason; ChangedAt = now; Version = Guid.NewGuid();
    }
}
