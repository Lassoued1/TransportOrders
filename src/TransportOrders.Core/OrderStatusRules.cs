namespace TransportOrders.Core;

/// <summary>
/// Single source of truth for the allowed status transitions of a transport order.
/// Starting to move (Planned → InTransit) additionally requires the order to be loaded on a
/// <see cref="Transport"/>; see <see cref="TransportOrder.CanTransitionTo"/>.
/// </summary>
public static class OrderStatusRules
{
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> AllowedTransitions =
    [
        (OrderStatus.Planned, OrderStatus.InTransit),
        (OrderStatus.InTransit, OrderStatus.Done),
        (OrderStatus.Planned, OrderStatus.Cancelled),
        (OrderStatus.InTransit, OrderStatus.Cancelled),
        (OrderStatus.Planned, OrderStatus.OnHold),
        (OrderStatus.OnHold, OrderStatus.Planned),
        (OrderStatus.OnHold, OrderStatus.Cancelled),
    ];

    public static bool IsAllowed(OrderStatus from, OrderStatus to) => AllowedTransitions.Contains((from, to));

    public static bool IsTerminal(OrderStatus status) => status is OrderStatus.Done or OrderStatus.Cancelled;
}
