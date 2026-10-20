namespace TransportOrders.Core;

public sealed partial class BasketEvent
{
    private BasketEvent() { }
    public BasketEvent(string action, string reference, string user, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        Action = action; Reference = reference; User = user; At = now;
    }
    public int Id { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Reference { get; private set; } = string.Empty;
    public string User { get; private set; } = string.Empty;
    public DateTime At { get; private set; }
}

public sealed partial class BasketReturn
{
    private BasketReturn() { }
    public BasketReturn(Basket basket, string target, string user, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        if (!basket.IsEmpty || basket.IsMoving || basket.LocationId is null || basket.LocationId == target)
            throw new TransportRuleException("An empty return needs a stationary empty basket and a different destination.");
        Id = Guid.NewGuid(); BasketNumber = basket.Number; ContentSequence = basket.ContentSequence;
        SourceLocation = basket.LocationId; TargetLocation = target; StartedAt = now; StartedBy = user;
        Version = Guid.NewGuid();
    }
    public Guid Id { get; private set; }
    public string BasketNumber { get; private set; } = string.Empty;
    public int ContentSequence { get; private set; }
    public string SourceLocation { get; private set; } = string.Empty;
    public string TargetLocation { get; private set; } = string.Empty;
    public DateTime StartedAt { get; private set; }
    public string StartedBy { get; private set; } = string.Empty;
    public DateTime? ArrivedAt { get; private set; }
    public string? ArrivedBy { get; private set; }
    public Guid Version { get; private set; }
    public void Complete(string user, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        if (ArrivedAt is not null) throw new TransportRuleException("This empty return is already complete.");
        ArrivedAt = now; ArrivedBy = user; Version = Guid.NewGuid();
    }
}
