using TransportOrders.Core;
using static TransportOrders.Tests.TestOrders;

namespace TransportOrders.Tests;

public class TransportTests
{
    private static readonly DateTime Now = CreatedAt.AddHours(1);

    [Fact]
    public void Full_load_moves_every_order_and_records_the_transport_on_each()
    {
        var a = New("TO-2026-0001", metres: 5.5m, tonnes: 15m);
        var b = New("TO-2026-0002", metres: 5.5m, tonnes: 15m);

        var transport = Transport.Dispatch("TR-2026-0001", Carrier, [a, b], "driver", Now);

        Assert.Equal((11m, 30m, 2), (transport.LoadedLength, transport.LoadedWeight, transport.OrderCount));
        Assert.Equal(11m / 12m, transport.FillRate);
        Assert.False(transport.IsPartLoad);
        Assert.Null(transport.PartLoadReason);
        Assert.All(new[] { a, b }, o =>
        {
            Assert.Equal(OrderStatus.InTransit, o.Status);
            Assert.Equal("TR-2026-0001", o.TransportNumber);
            Assert.Equal("TR-2026-0001", o.History[^1].Note);
        });
    }

    [Fact]
    public void A_transport_never_leaves_empty()
    {
        var ex = Assert.Throws<TransportRuleException>(() => Transport.Dispatch("TR-2026-0001", Carrier, [], "driver", Now));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void A_part_load_needs_a_recorded_reason()
    {
        var order = New(metres: 3m);

        var ex = Assert.Throws<PartLoadNotJustifiedException>(() => Transport.Dispatch("TR-2026-0001", Carrier, [order], "driver", Now));
        Assert.Equal(0.25m, ex.FillRate);
        Assert.Equal(OrderStatus.Planned, order.Status);

        var transport = Transport.Dispatch("TR-2026-0001", Carrier, [order], "driver", Now, PartLoadReason.UrgentMaterial);
        Assert.True(transport.IsPartLoad);
        Assert.Equal(PartLoadReason.UrgentMaterial, transport.PartLoadReason);
    }

    [Theory]
    [InlineData(6.5, 5)]    // 13 m on a 12 m bed
    [InlineData(5.5, 21)]   // 42 t on a 40 t vehicle
    public void Loading_length_and_payload_cannot_be_exceeded_even_with_a_reason(decimal metresEach, decimal tonnesEach)
    {
        var a = New("TO-2026-0001", metres: metresEach, tonnes: tonnesEach);
        var b = New("TO-2026-0002", metres: metresEach, tonnes: tonnesEach);

        Assert.Throws<TransportRuleException>(() =>
            Transport.Dispatch("TR-2026-0001", Carrier, [a, b], "driver", Now, PartLoadReason.EndOfShift));
        Assert.All(new[] { a, b }, o => Assert.Equal(OrderStatus.Planned, o.Status));
    }

    [Fact]
    public void Orders_of_one_transport_share_one_route()
    {
        var a = New("TO-2026-0001", metres: 6m);
        var b = New("TO-2026-0002", metres: 5m, to: "UT testing");

        var ex = Assert.Throws<TransportRuleException>(() => Transport.Dispatch("TR-2026-0001", Carrier, [a, b], "driver", Now));

        Assert.Contains("same route", ex.Message);
    }

    [Fact]
    public void Material_on_hold_or_already_loaded_cannot_be_loaded()
    {
        var held = In(OrderStatus.OnHold);
        var moving = In(OrderStatus.InTransit);

        Assert.Throws<TransportRuleException>(() => Transport.Dispatch("TR-2026-0002", ExactFit(held), [held], "driver", Now));
        Assert.Throws<TransportRuleException>(() => Transport.Dispatch("TR-2026-0002", ExactFit(moving), [moving], "driver", Now));
        Assert.Equal(OrderStatus.OnHold, held.Status);
    }

    [Fact]
    public void Completing_a_transport_marks_its_orders_done_and_leaves_cancelled_ones_cancelled()
    {
        var a = New("TO-2026-0001", metres: 5.5m);
        var b = New("TO-2026-0002", metres: 5.5m);
        var transport = Transport.Dispatch("TR-2026-0001", Carrier, [a, b], "driver", Now);
        b.TransitionTo(OrderStatus.Cancelled, "planner", Now.AddMinutes(5));

        transport.Complete([a, b], "driver", Now.AddMinutes(20));

        Assert.Equal((OrderStatus.Done, OrderStatus.Cancelled), (a.Status, b.Status));
        Assert.Equal((TransportStatus.Done, Now.AddMinutes(20)), (transport.Status, transport.CompletedAt));
        Assert.Throws<TransportRuleException>(() => transport.Complete([a, b], "driver", Now.AddMinutes(30)));
    }

    [Fact]
    public void Transport_numbers_follow_the_plant_format()
    {
        Assert.Throws<ArgumentException>(() => Transport.Dispatch("T-1", ExactFit(New()), [New()], "driver", Now));
    }
}
