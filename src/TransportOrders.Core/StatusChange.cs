namespace TransportOrders.Core;

/// <summary>One entry of an order's audit trail: who changed the status, when, from what, and why.</summary>
public sealed class StatusChange
{
    // Used by EF Core.
    private StatusChange()
    {
    }

    internal StatusChange(OrderStatus? fromStatus, OrderStatus toStatus, DateTime changedAtUtc, string changedBy, string? note)
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAt = changedAtUtc;
        ChangedBy = changedBy;
        Note = note;
    }

    public int Id { get; private set; }

    /// <summary>Null for the creation entry.</summary>
    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public string ChangedBy { get; private set; } = string.Empty;

    /// <summary>Hold reason, transport number the order was loaded on, or other context.</summary>
    public string? Note { get; private set; }
}
