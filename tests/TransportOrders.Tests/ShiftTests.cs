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
    public void Summary_counts_only_changes_inside_the_current_shift()
    {
        var created = new DateTime(2026, 3, 2, 5, 0, 0, DateTimeKind.Utc);  // night shift
        var now = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);     // early shift, 06:00–14:00 UTC

        var startedBeforeShift = Order("TO-2026-0001", created, dueAt: created.AddHours(3));
        startedBeforeShift.TransitionTo(OrderStatus.InTransit, "driver", created.AddMinutes(30));
        startedBeforeShift.TransitionTo(OrderStatus.Done, "driver", now.AddHours(-3));        // done 07:00, 90 min transit

        var doneInShift = Order("TO-2026-0002", created, dueAt: created.AddHours(8));
        doneInShift.TransitionTo(OrderStatus.InTransit, "driver", now.AddHours(-2));          // 08:00
        doneInShift.TransitionTo(OrderStatus.Done, "driver", now.AddHours(-1).AddMinutes(-30)); // 08:30, 30 min transit

        var overdueUrgent = Order("TO-2026-0003", created, dueAt: now.AddHours(-1), OrderPriority.Urgent);
        var cancelled = Order("TO-2026-0004", created, dueAt: now.AddHours(1));
        cancelled.TransitionTo(OrderStatus.Cancelled, "planner", now.AddMinutes(-10));
        var openOnTime = Order("TO-2026-0005", created, dueAt: now.AddHours(2));

        var summary = ShiftSummary.Calculate(
            [startedBeforeShift, doneInShift, overdueUrgent, cancelled, openOnTime], now, TimeZoneInfo.Utc);

        Assert.Equal(ShiftKind.Early, summary.Shift.Kind);
        Assert.Equal(1, summary.Started);
        Assert.Equal(2, summary.Completed);
        Assert.Equal(1, summary.Cancelled);
        Assert.Equal(TimeSpan.FromMinutes(60), summary.AverageTransitTime);       // (90 + 30) / 2
        Assert.Equal(2, summary.OpenOrders);
        Assert.Equal(1, summary.OverdueOrders);
        Assert.Equal(1, summary.UrgentOpenOrders);
    }

    [Fact]
    public void Summary_without_completed_orders_has_no_average()
    {
        var now = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

        var summary = ShiftSummary.Calculate([], now, TimeZoneInfo.Utc);

        Assert.Null(summary.AverageTransitTime);
        Assert.Equal(0, summary.OpenOrders);
    }

    private static TransportOrder Order(string number, DateTime created, DateTime dueAt,
        OrderPriority priority = OrderPriority.Normal) =>
        new(number, "H-26031", "Profile 4711", 50, "Press 1", "Aging Oven", priority, dueAt, created, "planner");
}
