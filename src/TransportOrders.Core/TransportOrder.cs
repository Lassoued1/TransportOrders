using System.Text.RegularExpressions;

namespace TransportOrders.Core;

/// <summary>
/// An internal transport order moving material between two locations of the plant.
/// The status can only change through <see cref="TransitionTo(OrderStatus)"/>.
/// </summary>
public sealed partial class TransportOrder
{
    // Used by EF Core to materialize entities; properties are then set through their private setters.
    private TransportOrder()
    {
    }

    public TransportOrder(
        string orderNumber,
        string material,
        int quantity,
        string sourceLocation,
        string targetLocation,
        DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(orderNumber) || !OrderNumberPattern().IsMatch(orderNumber))
        {
            throw new ArgumentException("Order number must follow the format TO-YYYY-NNNN.", nameof(orderNumber));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        OrderNumber = orderNumber;
        Material = Required(material, nameof(material));
        Quantity = quantity;
        SourceLocation = Required(sourceLocation, nameof(sourceLocation));
        TargetLocation = Required(targetLocation, nameof(targetLocation));
        Status = OrderStatus.Planned;
        CreatedAt = createdAtUtc;
        UpdatedAt = createdAtUtc;
    }

    public int Id { get; private set; }

    public string OrderNumber { get; private set; } = string.Empty;

    public string Material { get; private set; } = string.Empty;

    /// <summary>Quantity in pieces.</summary>
    public int Quantity { get; private set; }

    public string SourceLocation { get; private set; } = string.Empty;

    public string TargetLocation { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public bool CanTransitionTo(OrderStatus newStatus) => OrderStatusRules.IsAllowed(Status, newStatus);

    public void TransitionTo(OrderStatus newStatus) => TransitionTo(newStatus, DateTime.UtcNow);

    public void TransitionTo(OrderStatus newStatus, DateTime utcNow)
    {
        EnsureUtc(utcNow, nameof(utcNow));

        if (!CanTransitionTo(newStatus))
        {
            throw new InvalidStatusTransitionException(OrderNumber, Status, newStatus);
        }

        Status = newStatus;
        UpdatedAt = utcNow;
    }

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
}
