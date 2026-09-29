using TransportOrders.Core;
using static TransportOrders.Tests.TestOrders;

namespace TransportOrders.Tests;

public class TraceabilityTests
{
    [Fact]
    public void History_records_creation_hold_release_and_transport_with_user_time_and_note()
    {
        var order = New();
        order.TransitionTo(OrderStatus.OnHold, "qa", CreatedAt.AddMinutes(5), "Surface cracks");
        order.TransitionTo(OrderStatus.Planned, "qa", CreatedAt.AddMinutes(20));
        var transport = Transport.Dispatch("TR-2026-0007", ExactFit(order), [order], "driver", CreatedAt.AddMinutes(30));
        transport.Complete([order], "driver", CreatedAt.AddMinutes(60));

        Assert.Collection(
            order.History,
            h => Assert.Equal((null, OrderStatus.Planned, CreatedAt, "planner", null), (h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy, h.Note)),
            h => Assert.Equal((OrderStatus.Planned, OrderStatus.OnHold, "qa", "Surface cracks"), (h.FromStatus, h.ToStatus, h.ChangedBy, h.Note)),
            h => Assert.Equal((OrderStatus.OnHold, OrderStatus.Planned, "qa"), (h.FromStatus, h.ToStatus, h.ChangedBy)),
            h => Assert.Equal((OrderStatus.Planned, OrderStatus.InTransit, "TR-2026-0007"), (h.FromStatus, h.ToStatus, h.Note)),
            h => Assert.Equal((OrderStatus.InTransit, OrderStatus.Done, CreatedAt.AddMinutes(60)), (h.FromStatus, h.ToStatus, h.ChangedAt)));
        Assert.Equal(CreatedAt.AddMinutes(30), order.ReachedAt(OrderStatus.InTransit));
    }

    [Fact]
    public void Hold_needs_a_reason_which_stays_visible_while_on_hold()
    {
        var order = New();

        Assert.Throws<ArgumentException>(() => order.TransitionTo(OrderStatus.OnHold, "qa", "  "));
        Assert.Equal(OrderStatus.Planned, order.Status);

        order.TransitionTo(OrderStatus.OnHold, "qa", "Hardness below specification");
        Assert.Equal("Hardness below specification", order.HoldReason);

        order.TransitionTo(OrderStatus.Planned, "qa");
        Assert.Null(order.HoldReason);
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
    public void Heat_and_piece_numbers_are_normalised_and_must_belong_together()
    {
        var order = new TransportOrder("TO-2026-0002", " h-26044 ", "h-26044-1a", "H-26044-1", "1.3343 bar", 2, 4m, 3m,
            "Forge", "Heat treatment", OrderPriority.Normal, CreatedAt.AddHours(1), CreatedAt, "planner");

        Assert.Equal(("H-26044", "H-26044-1A", "H-26044-1"), (order.HeatNumber, order.PieceNumber, order.ParentPieceNumber));
        Assert.Throws<ArgumentException>(() => new TransportOrder("TO-2026-0003", "H-26044", "H-99999-1", null,
            "1.3343 bar", 2, 4m, 3m, "Forge", "Heat treatment", OrderPriority.Normal, CreatedAt.AddHours(1), CreatedAt, "planner"));
        Assert.Throws<ArgumentException>(() => new TransportOrder("TO-2026-0004", "H-26044", "H-26044-1", "H-26044-1",
            "1.3343 bar", 2, 4m, 3m, "Forge", "Heat treatment", OrderPriority.Normal, CreatedAt.AddHours(1), CreatedAt, "planner"));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(101, 3)]
    [InlineData(5, 0)]
    [InlineData(5, 31)]
    public void Weight_and_loading_length_must_be_realistic(decimal tonnes, decimal metres)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => New(tonnes: tonnes, metres: metres));
    }

    [Fact]
    public void Due_time_must_follow_creation()
    {
        Assert.Throws<ArgumentException>(() => New(dueAt: CreatedAt));
    }

    [Theory]
    [InlineData(OrderStatus.Planned, 5, true)]
    [InlineData(OrderStatus.OnHold, 5, true)]
    [InlineData(OrderStatus.InTransit, 5, true)]
    [InlineData(OrderStatus.Planned, -5, false)]
    [InlineData(OrderStatus.Done, 5, false)]
    [InlineData(OrderStatus.Cancelled, 5, false)]
    public void Only_open_orders_past_their_due_time_are_overdue(OrderStatus status, int minutesAfterDue, bool expected)
    {
        var order = In(status);

        Assert.Equal(expected, order.IsOverdue(order.DueAt.AddMinutes(minutesAfterDue)));
    }

    [Fact]
    public void Genealogy_links_ingots_to_the_bars_forged_from_them()
    {
        TransportOrder Piece(string number, string piece, string? parent, string material) =>
            new(number, "H-26031", piece, parent, material, 1, 5m, 3m, "Forge", "Heat treatment",
                OrderPriority.Normal, CreatedAt.AddHours(4), CreatedAt, "planner");
        var orders = new[]
        {
            Piece("TO-2026-0001", "H-26031-1", null, "1.2379 ingot"),
            Piece("TO-2026-0002", "H-26031-1A", "H-26031-1", "1.2379 forged bar"),
            Piece("TO-2026-0003", "H-26031-1B", "H-26031-1", "1.2379 forged bar"),
            Piece("TO-2026-0004", "H-26031-2A", "H-26031-2", "1.2379 forged bar"),   // parent ingot has no order
            new TransportOrder("TO-2026-0005", "H-26044", "H-26044-1", null, "1.3343 ingot", 1, 5m, 3m, "Forge",
                "Heat treatment", OrderPriority.Normal, CreatedAt.AddHours(4), CreatedAt, "planner"),
        };

        var tree = HeatGenealogy.Build("h-26031", orders);

        Assert.Equal("H-26031", tree.HeatNumber);
        Assert.Equal(5, tree.PieceCount);
        Assert.Equal(["H-26031-1", "H-26031-2"], tree.Roots.Select(r => r.PieceNumber));
        Assert.Equal(["H-26031-1A", "H-26031-1B"], tree.Roots[0].Children.Select(c => c.PieceNumber));
        Assert.Equal("1.2379 ingot", tree.Roots[0].Material);
        Assert.Null(tree.Roots[1].Material);
        Assert.Empty(tree.Roots[1].Orders);
        Assert.Equal("TO-2026-0004", Assert.Single(tree.Roots[1].Children).Orders.Single().OrderNumber);
    }
}
