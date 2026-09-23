using System.Text.RegularExpressions;

namespace TransportOrders.Core;

/// <summary>
/// An internal transport order moving material between two locations of the plant.
/// The status can only change through <see cref="TransitionTo(OrderStatus, string, DateTime)"/>,
/// which also records every change in <see cref="History"/> for traceability.
/// </summary>
public sealed partial class TransportOrder
{
    private readonly List<StatusChange> _history = [];

    // Used by EF Core to materialize entities; properties are then set through their private setters.
    private TransportOrder()
    {
    }

    public TransportOrder(
        string orderNumber,
        string heatNumber,
        string material,
        int quantity,
        string sourceLocation,
        string targetLocation,
        OrderPriority priority,
        DateTime dueAtUtc,
        DateTime createdAtUtc,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || !OrderNumberPattern().IsMatch(orderNumber))
        {
            throw new ArgumentException("Order number must follow the format TO-YYYY-NNNN.", nameof(orderNumber));
        }

        if (string.IsNullOrWhiteSpace(heatNumber) || !HeatNumberPattern().IsMatch(heatNumber.Trim()))
        {
            throw new ArgumentException(
                "Heat number must contain 3 to 20 letters, digits or dashes.", nameof(heatNumber));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        EnsureUtc(dueAtUtc, nameof(dueAtUtc));
        if (dueAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Due time must be after the creation time.", nameof(dueAtUtc));
        }

        OrderNumber = orderNumber;
        HeatNumber = heatNumber.Trim().ToUpperInvariant();
        Material = Required(material, nameof(material));
        Quantity = quantity;
        SourceLocation = Required(sourceLocation, nameof(sourceLocation));
        TargetLocation = Required(targetLocation, nameof(targetLocation));
        Priority = priority;
        DueAt = dueAtUtc;
        Status = OrderStatus.Planned;
        CreatedAt = createdAtUtc;
        UpdatedAt = createdAtUtc;
        _history.Add(new StatusChange(null, OrderStatus.Planned, createdAtUtc, Required(createdBy, nameof(createdBy))));
    }

    public int Id { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Heat (cast) number of the material, used to trace every order made from the same melt.</summary>
    public string HeatNumber { get; private set; } = string.Empty;

    public string Material { get; private set; } = string.Empty;

    /// <summary>Quantity in pieces.</summary>
    public int Quantity { get; private set; }

    public string SourceLocation { get; private set; } = string.Empty;

    public string TargetLocation { get; private set; } = string.Empty;

    public OrderPriority Priority { get; private set; }

    /// <summary>Time (UTC) by which the material must have arrived.</summary>
    public DateTime DueAt { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>Every status the order went through, oldest first, starting with its creation.</summary>
    public IReadOnlyList<StatusChange> History => _history;

    /// <summary>True while the order still has to be moved (Planned or InTransit).</summary>
    public bool IsOpen => !OrderStatusRules.IsTerminal(Status);

    public bool IsOverdue(DateTime utcNow) => IsOpen && utcNow > DueAt;

    public bool CanTransitionTo(OrderStatus newStatus) => OrderStatusRules.IsAllowed(Status, newStatus);

    public void TransitionTo(OrderStatus newStatus, string changedBy) =>
        TransitionTo(newStatus, changedBy, DateTime.UtcNow);

    public void TransitionTo(OrderStatus newStatus, string changedBy, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        var user = Required(changedBy, nameof(changedBy));

        if (!CanTransitionTo(newStatus))
        {
            throw new InvalidStatusTransitionException(OrderNumber, Status, newStatus);
        }

        _history.Add(new StatusChange(Status, newStatus, utcNow, user));
        Status = newStatus;
        UpdatedAt = utcNow;
    }

    /// <summary>Time of the most recent change to <paramref name="status"/>, if the order reached it.</summary>
    public DateTime? ReachedAt(OrderStatus status) =>
        _history.LastOrDefault(h => h.ToStatus == status)?.ChangedAt;

    private static string Required(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();

    private static void EnsureUtc(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Date must be expressed in UTC.", paramName);
        }
    }

    [GeneratedRegex(@"^TO-\d{4}-\d{4}$")]
    private static partial Regex OrderNumberPattern();

    [GeneratedRegex(@"^[A-Za-z0-9-]{3,20}$")]
    private static partial Regex HeatNumberPattern();
}
