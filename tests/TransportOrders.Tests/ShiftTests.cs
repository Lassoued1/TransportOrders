using TransportOrders.Core;

namespace TransportOrders.Tests;

public class ShiftTests
{
    private static readonly DateTime Day = new(2026, 3, 2);

    [Theory]
    [InlineData(6, 0, ShiftKind.Early, 6, 14)]
    [InlineData(13, 59, ShiftKind.Early, 6, 14)]
    [InlineData(14, 0, ShiftKind.Late, 14, 22)]
    [InlineData(22, 0, ShiftKind.Night, 22, 30)]
    [InlineData(5, 59, ShiftKind.Night, -2, 6)]
    public void Shift_boundaries(int hour, int minute, ShiftKind kind, int startHour, int endHour)
    {
        var shift = Shift.At(Day.AddHours(hour).AddMinutes(minute));

        Assert.Equal(kind, shift.Kind);
        Assert.Equal(Day.AddHours(startHour), shift.StartLocal);
        Assert.Equal(Day.AddHours(endHour), shift.EndLocal);
    }

    [Fact]
    public void Summary_counts_only_what_happened_inside_the_current_shift()
    {
        var created = new DateTime(2026, 3, 2, 5, 0, 0, DateTimeKind.Utc);  // night shift
        var now = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);     // early shift, 06:00–14:00 UTC
        var carrier = new Vehicle(VehicleType.HeavyLoadCarrier, 12m, 40m);

        // Dispatched in the night shift (full), completed in the early shift after 90 minutes.
        var night = Order("TO-2026-0001", created, dueAt: created.AddHours(3), metres: 11m);
        var nightTrip = Transport.Dispatch("TR-2026-0001", carrier, [night], "driver", created.AddMinutes(30));
        nightTrip.Complete([night], "driver", now.AddHours(-3));                                   // 07:00

        // Dispatched and completed in the early shift as a part load, 30 minutes on the way.
        var early = Order("TO-2026-0002", created, dueAt: created.AddHours(8), metres: 6m);
        var earlyTrip = Transport.Dispatch("TR-2026-0002", carrier, [early], "driver", now.AddHours(-2), PartLoadReason.UrgentMaterial);
        earlyTrip.Complete([early], "driver", now.AddHours(-1).AddMinutes(-30));                  // 08:30

        var overdueUrgent = Order("TO-2026-0003", created, dueAt: now.AddHours(-1), priority: OrderPriority.Urgent);
        var cancelled = Order("TO-2026-0004", created, dueAt: now.AddHours(1));
        cancelled.TransitionTo(OrderStatus.Cancelled, "planner", now.AddMinutes(-10));
        var held = Order("TO-2026-0005", created, dueAt: now.AddHours(2));
        held.TransitionTo(OrderStatus.OnHold, "qa", now.AddMinutes(-20), "Surface cracks");

        var summary = ShiftSummary.Calculate(
            [night, early, overdueUrgent, cancelled, held], [nightTrip, earlyTrip], now, TimeZoneInfo.Utc);

        Assert.Equal(ShiftKind.Early, summary.Shift.Kind);
        Assert.Equal((1, 2, 1), (summary.Started, summary.Completed, summary.Cancelled));
        Assert.Equal(TimeSpan.FromMinutes(60), summary.AverageTransitTime);                      // (90 + 30) / 2
        Assert.Equal((1, 0.5m, 1), (summary.Trips, summary.AverageFillRate, summary.PartLoads));   // only the early trip
        Assert.Equal((2, 1, 1, 1), (summary.OpenOrders, summary.OverdueOrders, summary.UrgentOpenOrders, summary.OnHoldOrders));
    }

    [Fact]
    public void Summary_without_activity_has_no_averages()
    {
        var now = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

        var summary = ShiftSummary.Calculate([], [], now, TimeZoneInfo.Utc);

        Assert.Null(summary.AverageTransitTime);
        Assert.Null(summary.AverageFillRate);
        Assert.Equal((0, 0), (summary.OpenOrders, summary.Trips));
    }

    private static TransportOrder Order(string number, DateTime created, DateTime dueAt,
        OrderPriority priority = OrderPriority.Normal, decimal metres = 3m) =>
        new(number, "H-26031", "H-26031-1A", null, "1.2379 forged bar", 2, 5m, metres, "Forge", "Heat treatment",
            priority, dueAt, created, "planner");
}
