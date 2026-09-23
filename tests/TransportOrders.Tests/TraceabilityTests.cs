using TransportOrders.Core;
using static TransportOrders.Tests.TestOrders;

namespace TransportOrders.Tests;

public class TraceabilityTests
{
    [Fact]
    public void History_records_creation_and_every_transition_with_user_and_time()
    {
        var order = New();
        order.TransitionTo(OrderStatus.InTransit, "driver.a", CreatedAt.AddMinutes(10));
        order.TransitionTo(OrderStatus.Done, "driver.b", CreatedAt.AddMinutes(40));

        Assert.Collection(
            order.History,
            h => Assert.Equal((null, OrderStatus.Planned, CreatedAt, "planner"), (h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy)),
            h => Assert.Equal((OrderStatus.Planned, OrderStatus.InTransit, CreatedAt.AddMinutes(10), "driver.a"), (h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy)),
            h => Assert.Equal((OrderStatus.InTransit, OrderStatus.Done, CreatedAt.AddMinutes(40), "driver.b"), (h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy)));
        Assert.Equal(CreatedAt.AddMinutes(10), order.ReachedAt(OrderStatus.InTransit));
    }

    [Fact]
    public void Rejected_transition_leaves_no_trace_in_history()
    {
        var order = In(OrderStatus.Done);
        var entries = order.History.Count;

        Assert.Throws<InvalidStatusTransitionException>(() => order.TransitionTo(OrderStatus.Cancelled, "driver"));

        Assert.Equal(entries, order.History.Count);
    }

    [Fact]
    public void Heat_number_is_normalised_and_validated()
    {
        var order = new TransportOrder("TO-2026-0002", " h-26044 ", "Billet 6082", 10, "Warehouse A", "Press 2",
            OrderPriority.Normal, CreatedAt.AddHours(1), CreatedAt, "planner");

        Assert.Equal("H-26044", order.HeatNumber);
        Assert.Throws<ArgumentException>(() => new TransportOrder("TO-2026-0003", "H 1/2", "Billet 6082", 10,
            "Warehouse A", "Press 2", OrderPriority.Normal, CreatedAt.AddHours(1), CreatedAt, "planner"));
    }

    [Fact]
    public void Due_time_must_follow_creation()
    {
        Assert.Throws<ArgumentException>(() => New(dueAt: CreatedAt));
    }

    [Theory]
    [InlineData(OrderStatus.Planned, 5, true)]
    [InlineData(OrderStatus.InTransit, 5, true)]
    [InlineData(OrderStatus.Planned, -5, false)]
    [InlineData(OrderStatus.Done, 5, false)]
    [InlineData(OrderStatus.Cancelled, 5, false)]
    public void Only_open_orders_past_their_due_time_are_overdue(OrderStatus status, int minutesAfterDue, bool expected)
    {
        var order = In(status);

        Assert.Equal(expected, order.IsOverdue(order.DueAt.AddMinutes(minutesAfterDue)));
    }
}
