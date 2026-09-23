namespace TransportOrders.Core;

/// <summary>One entry of an order's audit trail: who changed the status, when, and from what.</summary>
public sealed class StatusChange
{
    // Used by EF Core.
    private StatusChange()
    {
    }

    internal StatusChange(OrderStatus? fromStatus, OrderStatus toStatus, DateTime changedAtUtc, string changedBy)
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAt = changedAtUtc;
        ChangedBy = changedBy;
    }

    public int Id { get; private set; }

    /// <summary>Null for the creation entry.</summary>
    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public string ChangedBy { get; private set; } = string.Empty;
}
