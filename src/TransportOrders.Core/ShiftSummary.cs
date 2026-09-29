namespace TransportOrders.Core;

/// <summary>Key figures for the current shift, computed from the orders' history and the transports.</summary>
/// <param name="Started">Orders moved to InTransit during the shift.</param>
/// <param name="Completed">Orders moved to Done during the shift.</param>
/// <param name="Cancelled">Orders cancelled during the shift.</param>
/// <param name="AverageTransitTime">Average InTransit → Done time of the orders completed during the shift.</param>
/// <param name="Trips">Transports dispatched during the shift.</param>
/// <param name="AverageFillRate">Average share of loading length used by those transports.</param>
/// <param name="PartLoads">Transports of the shift that left below the full-load threshold (with a recorded reason).</param>
/// <param name="OpenOrders">Orders still Planned, OnHold or InTransit right now.</param>
/// <param name="OverdueOrders">Open orders past their due time right now.</param>
/// <param name="UrgentOpenOrders">Open orders with urgent priority right now.</param>
/// <param name="OnHoldOrders">Orders blocked by quality assurance right now.</param>
public sealed record ShiftSummary(
    Shift Shift,
    int Started,
    int Completed,
    int Cancelled,
    TimeSpan? AverageTransitTime,
    int Trips,
    decimal? AverageFillRate,
    int PartLoads,
    int OpenOrders,
    int OverdueOrders,
    int UrgentOpenOrders,
    int OnHoldOrders)
{
    public static ShiftSummary Calculate(
        IEnumerable<TransportOrder> orders, IEnumerable<Transport> transports, DateTime utcNow, TimeZoneInfo plantTimeZone)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(transports);
        ArgumentNullException.ThrowIfNull(plantTimeZone);

        var shift = Shift.At(TimeZoneInfo.ConvertTimeFromUtc(utcNow, plantTimeZone));
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(shift.StartLocal, plantTimeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(shift.EndLocal, plantTimeZone);
        bool InShift(DateTime t) => t >= startUtc && t < endUtc;

        int started = 0, completed = 0, cancelled = 0, open = 0, overdue = 0, urgentOpen = 0, onHold = 0;
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

            if (order.Status == OrderStatus.OnHold)
            {
                onHold++;
            }
        }

        var trips = transports.Where(t => InShift(t.DispatchedAt)).ToList();

        TimeSpan? averageTransit = transitTimes.Count == 0
            ? null
            : TimeSpan.FromTicks((long)transitTimes.Average(t => t.Ticks));
        decimal? averageFill = trips.Count == 0 ? null : trips.Average(t => t.FillRate);

        return new ShiftSummary(shift, started, completed, cancelled, averageTransit,
            trips.Count, averageFill, trips.Count(t => t.IsPartLoad), open, overdue, urgentOpen, onHold);
    }
}
