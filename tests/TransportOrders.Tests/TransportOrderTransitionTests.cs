using TransportOrders.Core;
using static TransportOrders.Tests.TestOrders;

namespace TransportOrders.Tests;

public class TransportOrderTransitionTests
{
    [Fact]
    public void New_order_starts_planned_with_matching_timestamps()
    {
        var order = New();

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
        var order = In(from);

        Assert.True(order.CanTransitionTo(to));
        order.TransitionTo(to, "driver", CreatedAt.AddHours(1));

        Assert.Equal(to, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Done, OrderStatus.InTransit)]
    [InlineData(OrderStatus.Done, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Planned)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.InTransit)]
    public void Terminal_status_cannot_be_left(OrderStatus terminal, OrderStatus target)
    {
        var order = In(terminal);

        Assert.False(order.CanTransitionTo(target));
        Assert.Throws<InvalidStatusTransitionException>(() => order.TransitionTo(target, "driver"));
        Assert.Equal(terminal, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Planned, OrderStatus.Done)]
    [InlineData(OrderStatus.InTransit, OrderStatus.Planned)]
    [InlineData(OrderStatus.Planned, OrderStatus.Planned)]
    [InlineData(OrderStatus.InTransit, OrderStatus.InTransit)]
    public void Skipping_or_repeating_a_status_is_rejected_with_a_clear_message(OrderStatus from, OrderStatus to)
    {
        var order = In(from);

        var ex = Assert.Throws<InvalidStatusTransitionException>(() => order.TransitionTo(to, "driver"));

        Assert.Equal(from, ex.From);
        Assert.Equal(to, ex.To);
        Assert.Contains($"from {from} to {to}", ex.Message);
        Assert.Equal(from, order.Status);
    }

    [Fact]
    public void UpdatedAt_changes_on_success_and_is_kept_on_rejection()
    {
        var order = New();
        var startedAt = CreatedAt.AddMinutes(30);

        order.TransitionTo(OrderStatus.InTransit, "driver", startedAt);
        Assert.Equal(startedAt, order.UpdatedAt);
        Assert.Equal(CreatedAt, order.CreatedAt);

        Assert.Throws<InvalidStatusTransitionException>(
            () => order.TransitionTo(OrderStatus.Planned, "driver", startedAt.AddMinutes(30)));
        Assert.Equal(startedAt, order.UpdatedAt);
    }

    [Fact]
    public void Default_transition_uses_current_utc_time()
    {
        var order = New();
        var before = DateTime.UtcNow;

        order.TransitionTo(OrderStatus.InTransit, "driver");

        Assert.InRange(order.UpdatedAt, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, order.UpdatedAt.Kind);
    }
}
