using TransportOrders.Core;

namespace TransportOrders.Tests;

public class TransportOrderTransitionTests
{
    private static readonly DateTime CreatedAt = new(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);

    private static TransportOrder NewOrder() =>
        new("TO-2026-0001", "Billet 6060", 40, "Warehouse A", "Press 1", CreatedAt);

    private static TransportOrder OrderIn(OrderStatus status)
    {
        var order = NewOrder();
        var at = CreatedAt;
        switch (status)
        {
            case OrderStatus.InTransit:
                order.TransitionTo(OrderStatus.InTransit, at.AddMinutes(1));
                break;
            case OrderStatus.Done:
                order.TransitionTo(OrderStatus.InTransit, at.AddMinutes(1));
                order.TransitionTo(OrderStatus.Done, at.AddMinutes(2));
                break;
            case OrderStatus.Cancelled:
                order.TransitionTo(OrderStatus.Cancelled, at.AddMinutes(1));
                break;
        }

        return order;
    }

    [Fact]
    public void New_order_starts_planned_with_matching_timestamps()
    {
        var order = NewOrder();

        Assert.Equal(OrderStatus.Planned, order.Status);
        Assert.Equal(CreatedAt, order.CreatedAt);
        Assert.Equal(CreatedAt, order.UpdatedAt);
    }

    [Theory]
    [InlineData(OrderStatus.Planned, OrderStatus.InTransit)]
    [InlineData(OrderStatus.InTransit, OrderStatus.Done)]
    [InlineData(OrderStatus.Planned, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.InTransit, OrderStatus.Cancelled)]
    public void Allowed_transition_changes_status(OrderStatus from, OrderStatus to)
    {
        var order = OrderIn(from);

        Assert.True(order.CanTransitionTo(to));
        order.TransitionTo(to, CreatedAt.AddHours(1));

        Assert.Equal(to, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Done, OrderStatus.InTransit)]
    [InlineData(OrderStatus.Done, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Planned)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.InTransit)]
    public void Terminal_status_cannot_be_left(OrderStatus terminal, OrderStatus target)
    {
        var order = OrderIn(terminal);

        Assert.False(order.CanTransitionTo(target));
        Assert.Throws<InvalidStatusTransitionException>(() => order.TransitionTo(target));
        Assert.Equal(terminal, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Planned, OrderStatus.Done)]
    [InlineData(OrderStatus.InTransit, OrderStatus.Planned)]
    [InlineData(OrderStatus.Planned, OrderStatus.Planned)]
    [InlineData(OrderStatus.InTransit, OrderStatus.InTransit)]
    public void Skipping_or_repeating_a_status_is_rejected_with_a_clear_message(OrderStatus from, OrderStatus to)
    {
        var order = OrderIn(from);

        var ex = Assert.Throws<InvalidStatusTransitionException>(() => order.TransitionTo(to));

        Assert.Equal(from, ex.From);
        Assert.Equal(to, ex.To);
        Assert.Contains($"from {from} to {to}", ex.Message);
        Assert.Equal(from, order.Status);
    }

    [Fact]
    public void UpdatedAt_changes_on_success_and_is_kept_on_rejection()
    {
        var order = NewOrder();
        var startedAt = CreatedAt.AddMinutes(30);

        order.TransitionTo(OrderStatus.InTransit, startedAt);
        Assert.Equal(startedAt, order.UpdatedAt);
        Assert.Equal(CreatedAt, order.CreatedAt);

        Assert.Throws<InvalidStatusTransitionException>(
            () => order.TransitionTo(OrderStatus.Planned, startedAt.AddMinutes(30)));
        Assert.Equal(startedAt, order.UpdatedAt);
    }

    [Fact]
    public void Default_transition_uses_current_utc_time()
    {
        var order = NewOrder();
        var before = DateTime.UtcNow;

        order.TransitionTo(OrderStatus.InTransit);

        Assert.InRange(order.UpdatedAt, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, order.UpdatedAt.Kind);
    }
}
