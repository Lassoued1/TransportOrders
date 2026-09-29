using TransportOrders.Core;

namespace TransportOrders.Tests;

internal static class TestOrders
{
    public static readonly DateTime CreatedAt = new(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);

    public static readonly Vehicle Carrier = new(VehicleType.HeavyLoadCarrier, 12m, 40m);

    public static TransportOrder New(
        string number = "TO-2026-0001",
        OrderPriority priority = OrderPriority.Normal,
        DateTime? dueAt = null,
        decimal tonnes = 5m,
        decimal metres = 3m,
        string from = "Forge",
        string to = "Heat treatment") =>
        new(number, "H-26031", "H-26031-1A", "H-26031-1", "1.2379 forged bar", 2, tonnes, metres,
            from, to, priority, dueAt ?? CreatedAt.AddHours(4), CreatedAt, "planner");

    /// <summary>A vehicle exactly as long and strong as the order, so the order alone is a full load.</summary>
    public static Vehicle ExactFit(TransportOrder order) => new(VehicleType.Forklift, order.LoadingLength, order.Weight);

    /// <summary>An order brought to <paramref name="status"/> through the domain rules, one minute per step.</summary>
    public static TransportOrder In(OrderStatus status)
    {
        var order = New();
        switch (status)
        {
            case OrderStatus.OnHold:
                order.TransitionTo(OrderStatus.OnHold, "qa", CreatedAt.AddMinutes(1), "Surface cracks");
                break;
            case OrderStatus.InTransit:
                Transport.Dispatch("TR-2026-0001", ExactFit(order), [order], "driver", CreatedAt.AddMinutes(1));
                break;
            case OrderStatus.Done:
                var transport = Transport.Dispatch("TR-2026-0001", ExactFit(order), [order], "driver", CreatedAt.AddMinutes(1));
                transport.Complete([order], "driver", CreatedAt.AddMinutes(2));
                break;
            case OrderStatus.Cancelled:
                order.TransitionTo(OrderStatus.Cancelled, "planner", CreatedAt.AddMinutes(1));
                break;
        }

        return order;
    }
}
