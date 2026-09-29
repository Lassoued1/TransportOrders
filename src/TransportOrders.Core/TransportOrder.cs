using System.Text.RegularExpressions;

namespace TransportOrders.Core;

/// <summary>
/// An internal transport order moving one piece of material (e.g. an ingot or a set of forged bars)
/// between two stations of the plant. Status changes go through <see cref="TransitionTo(OrderStatus, string, DateTime, string?)"/>
/// and are recorded in <see cref="History"/>; an order only starts moving as part of a <see cref="Transport"/>.
/// </summary>
public sealed partial class TransportOrder
{
    public const decimal MaxWeight = 100m;

    public const decimal MaxLoadingLength = 30m;

    private readonly List<StatusChange> _history = [];

    // Used by EF Core to materialize entities; properties are then set through their private setters.
    private TransportOrder()
    {
    }

    /// <param name="pieceNumber">Identifies the piece within its heat, e.g. H-26031-1A; must start with the heat number.</param>
    /// <param name="parentPieceNumber">The piece this one was made from (e.g. the ingot a bar was forged from), if any.</param>
    /// <param name="quantity">Number of pieces.</param>
    /// <param name="weight">Weight in tonnes.</param>
    /// <param name="loadingLength">Loading length the piece occupies on a vehicle bed, in metres.</param>
    public TransportOrder(
        string orderNumber,
        string heatNumber,
        string pieceNumber,
        string? parentPieceNumber,
        string material,
        int quantity,
        decimal weight,
        decimal loadingLength,
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

        HeatNumber = Identifier(heatNumber, nameof(heatNumber));
        PieceNumber = Identifier(pieceNumber, nameof(pieceNumber));
        ParentPieceNumber = parentPieceNumber is null ? null : Identifier(parentPieceNumber, nameof(parentPieceNumber));
        if (!PieceNumber.StartsWith(HeatNumber, StringComparison.Ordinal)
            || (ParentPieceNumber is not null && !ParentPieceNumber.StartsWith(HeatNumber, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Piece numbers must start with the heat number they belong to.", nameof(pieceNumber));
        }

        if (ParentPieceNumber == PieceNumber)
        {
            throw new ArgumentException("A piece cannot be its own parent.", nameof(parentPieceNumber));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (weight <= 0 || weight > MaxWeight)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), weight, $"Weight must be between 0 and {MaxWeight} t.");
        }

        if (loadingLength <= 0 || loadingLength > MaxLoadingLength)
        {
            throw new ArgumentOutOfRangeException(nameof(loadingLength), loadingLength,
                $"Loading length must be between 0 and {MaxLoadingLength} m.");
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        EnsureUtc(dueAtUtc, nameof(dueAtUtc));
        if (dueAtUtc <= createdAtUtc)
        {
            throw new ArgumentException("Due time must be after the creation time.", nameof(dueAtUtc));
        }

        OrderNumber = orderNumber;
        Material = Required(material, nameof(material));
        Quantity = quantity;
        Weight = weight;
        LoadingLength = loadingLength;
        SourceLocation = Required(sourceLocation, nameof(sourceLocation));
        TargetLocation = Required(targetLocation, nameof(targetLocation));
        Priority = priority;
        DueAt = dueAtUtc;
        Status = OrderStatus.Planned;
        CreatedAt = createdAtUtc;
        UpdatedAt = createdAtUtc;
        _history.Add(new StatusChange(null, OrderStatus.Planned, createdAtUtc, Required(createdBy, nameof(createdBy)), null));
    }

    public int Id { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    /// <summary>Heat (melt) number of the material, used to trace every piece made from the same melt.</summary>
    public string HeatNumber { get; private set; } = string.Empty;

    public string PieceNumber { get; private set; } = string.Empty;

    public string? ParentPieceNumber { get; private set; }

    public string Material { get; private set; } = string.Empty;

    /// <summary>Number of pieces.</summary>
    public int Quantity { get; private set; }

    /// <summary>Weight in tonnes.</summary>
    public decimal Weight { get; private set; }

    /// <summary>Loading length on a vehicle bed, in metres: what a full load is measured on.</summary>
    public decimal LoadingLength { get; private set; }

    public string SourceLocation { get; private set; } = string.Empty;

    public string TargetLocation { get; private set; } = string.Empty;

    public OrderPriority Priority { get; private set; }

    /// <summary>Time (UTC) by which the material must have arrived.</summary>
    public DateTime DueAt { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>The transport the order was loaded on, once it has started moving.</summary>
    public string? TransportNumber { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>Every status the order went through, oldest first, starting with its creation.</summary>
    public IReadOnlyList<StatusChange> History => _history;

    /// <summary>True while the order still has to be moved (Planned, OnHold or InTransit).</summary>
    public bool IsOpen => !OrderStatusRules.IsTerminal(Status);

    /// <summary>The reason given when the order was last put on hold, while it is on hold.</summary>
    public string? HoldReason => Status == OrderStatus.OnHold ? _history.LastOrDefault(h => h.ToStatus == OrderStatus.OnHold)?.Note : null;

    public bool IsOverdue(DateTime utcNow) => IsOpen && utcNow > DueAt;

    /// <summary>
    /// Whether <paramref name="newStatus"/> can be reached now. Moving to InTransit is only possible once the
    /// order is loaded on a transport, which is how the full-load rule cannot be bypassed.
    /// </summary>
    public bool CanTransitionTo(OrderStatus newStatus) =>
        OrderStatusRules.IsAllowed(Status, newStatus)
        && (newStatus != OrderStatus.InTransit || TransportNumber is not null);

    public void TransitionTo(OrderStatus newStatus, string changedBy, string? note = null) =>
        TransitionTo(newStatus, changedBy, DateTime.UtcNow, note);

    /// <param name="note">Why the status changed; required when putting an order on hold.</param>
    public void TransitionTo(OrderStatus newStatus, string changedBy, DateTime utcNow, string? note = null)
    {
        EnsureUtc(utcNow, nameof(utcNow));
        var user = Required(changedBy, nameof(changedBy));

        if (!OrderStatusRules.IsAllowed(Status, newStatus))
        {
            throw new InvalidStatusTransitionException(OrderNumber, Status, newStatus);
        }

        if (newStatus == OrderStatus.InTransit && TransportNumber is null)
        {
            throw new TransportRuleException($"Order {OrderNumber} can only start moving as part of a transport.");
        }

        if (newStatus == OrderStatus.OnHold && string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("A reason is required to put an order on hold.", nameof(note));
        }

        _history.Add(new StatusChange(Status, newStatus, utcNow, user, string.IsNullOrWhiteSpace(note) ? null : note.Trim()));
        Status = newStatus;
        UpdatedAt = utcNow;
    }

    /// <summary>Time of the most recent change to <paramref name="status"/>, if the order reached it.</summary>
    public DateTime? ReachedAt(OrderStatus status) =>
        _history.LastOrDefault(h => h.ToStatus == status)?.ChangedAt;

    /// <summary>Loads the order on a transport and starts moving it; only called by <see cref="Transport"/>.</summary>
    internal void Load(string transportNumber, string changedBy, DateTime utcNow)
    {
        if (Status != OrderStatus.Planned || TransportNumber is not null)
        {
            throw new TransportRuleException($"Order {OrderNumber} is {Status} and cannot be loaded.");
        }

        TransportNumber = transportNumber;
        TransitionTo(OrderStatus.InTransit, changedBy, utcNow, transportNumber);
    }

    private static string Required(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();

    private static string Identifier(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value) || !IdentifierPattern().IsMatch(value.Trim())
            ? throw new ArgumentException("Must contain 3 to 30 letters, digits or dashes.", paramName)
            : value.Trim().ToUpperInvariant();

    private static void EnsureUtc(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Date must be expressed in UTC.", paramName);
        }
    }

    [GeneratedRegex(@"^TO-\d{4}-\d{4}$")]
    private static partial Regex OrderNumberPattern();

    [GeneratedRegex(@"^[A-Za-z0-9-]{3,30}$")]
    private static partial Regex IdentifierPattern();
}
