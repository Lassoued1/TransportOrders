using TransportOrders.Core;

namespace TransportOrders.Tests;

internal static class TestOrders
{
    public static readonly DateTime CreatedAt = new(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);

    public static TransportOrder New(OrderPriority priority = OrderPriority.Normal, DateTime? dueAt = null) =>
        new("TO-2026-0001", "H-26031", "Billet 6060", 40, "Warehouse A", "Press 1",
            priority, dueAt ?? CreatedAt.AddHours(4), CreatedAt, "planner");

    /// <summary>An order brought to <paramref name="status"/> through valid transitions, one minute apart.</summary>
    public static TransportOrder In(OrderStatus status)
    {
        var order = New();
        switch (status)
        {
            case OrderStatus.InTransit:
                order.TransitionTo(OrderStatus.InTransit, "driver", CreatedAt.AddMinutes(1));
                break;
            case OrderStatus.Done:
                order.TransitionTo(OrderStatus.InTransit, "driver", CreatedAt.AddMinutes(1));
                order.TransitionTo(OrderStatus.Done, "driver", CreatedAt.AddMinutes(2));
                break;
            case OrderStatus.Cancelled:
                order.TransitionTo(OrderStatus.Cancelled, "planner", CreatedAt.AddMinutes(1));
                break;
        }

        return order;
    }
}
