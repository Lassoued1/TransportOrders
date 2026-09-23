namespace TransportOrders.Core;

/// <summary>Key figures for the current shift, computed from the orders' status history.</summary>
/// <param name="Started">Orders moved to InTransit during the shift.</param>
/// <param name="Completed">Orders moved to Done during the shift.</param>
/// <param name="Cancelled">Orders cancelled during the shift.</param>
/// <param name="AverageTransitTime">Average InTransit → Done time of the orders completed during the shift.</param>
/// <param name="OpenOrders">Orders still Planned or InTransit right now.</param>
/// <param name="OverdueOrders">Open orders past their due time right now.</param>
/// <param name="UrgentOpenOrders">Open orders with urgent priority right now.</param>
public sealed record ShiftSummary(
    Shift Shift,
    int Started,
    int Completed,
    int Cancelled,
    TimeSpan? AverageTransitTime,
    int OpenOrders,
    int OverdueOrders,
    int UrgentOpenOrders)
{
    public static ShiftSummary Calculate(IEnumerable<TransportOrder> orders, DateTime utcNow, TimeZoneInfo plantTimeZone)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(plantTimeZone);

        var shift = Shift.At(TimeZoneInfo.ConvertTimeFromUtc(utcNow, plantTimeZone));
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(shift.StartLocal, plantTimeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(shift.EndLocal, plantTimeZone);
        bool InShift(DateTime t) => t >= startUtc && t < endUtc;

        int started = 0, completed = 0, cancelled = 0, open = 0, overdue = 0, urgentOpen = 0;
        var transitTimes = new List<TimeSpan>();

        foreach (var order in orders)
        {
            foreach (var change in order.History.Where(h => InShift(h.ChangedAt)))
            {
                switch (change.ToStatus)
                {
                    case OrderStatus.InTransit:
                        started++;
                        break;
                    case OrderStatus.Done:
                        completed++;
                        if (order.ReachedAt(OrderStatus.InTransit) is { } startedAt)
                        {
                            transitTimes.Add(change.ChangedAt - startedAt);
                        }

                        break;
                    case OrderStatus.Cancelled:
                        cancelled++;
                        break;
                }
            }

            if (order.IsOpen)
            {
                open++;
                if (order.IsOverdue(utcNow))
                {
                    overdue++;
                }

                if (order.Priority == OrderPriority.Urgent)
                {
                    urgentOpen++;
                }
            }
        }

        TimeSpan? average = transitTimes.Count == 0
            ? null
            : TimeSpan.FromTicks((long)transitTimes.Average(t => t.Ticks));

        return new ShiftSummary(shift, started, completed, cancelled, average, open, overdue, urgentOpen);
    }
}
