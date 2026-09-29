namespace TransportOrders.Core;

/// <summary>A suggested trip: which vehicle takes which orders on one route.</summary>
public sealed record LoadProposal(Vehicle Vehicle, string SourceLocation, string TargetLocation,
    IReadOnlyList<TransportOrder> Orders, bool HasUrgentOrders)
{
    public decimal LoadedLength => Orders.Sum(o => o.LoadingLength);

    public decimal LoadedWeight => Orders.Sum(o => o.Weight);

    /// <summary>Share of the loading length in use.</summary>
    public decimal FillRate => LoadedLength / Vehicle.LoadingLength;

    public bool IsFull => FillRate >= Transport.FullLoadThreshold;

    /// <summary>The payload is (almost) used up although the loading length is not: short, heavy pieces.</summary>
    public bool IsWeightLimited => !IsFull && LoadedWeight >= Vehicle.Payload * Transport.FullLoadThreshold;

    /// <summary>The reason the dispatcher will most likely record for a part load.</summary>
    public PartLoadReason? SuggestedReason =>
        IsFull ? null
        : IsWeightLimited ? PartLoadReason.PayloadLimit
        : HasUrgentOrders ? PartLoadReason.UrgentMaterial
        : null;
}

/// <param name="Unplannable">Orders that fit no vehicle (too long or too heavy).</param>
public sealed record LoadPlan(IReadOnlyList<LoadProposal> Proposals, IReadOnlyList<TransportOrder> Unplannable);

/// <summary>
/// Groups the orders waiting to move into full loads, measured on loading length. Filling vehicles optimally
/// is a bin-packing problem (NP-hard in general), so the planner uses a heuristic that is exact for one
/// vehicle at a time:
/// <list type="number">
/// <item>Orders are grouped by route; only Planned orders can be loaded (on-hold material never moves).</item>
/// <item>Urgent and overdue orders are placed first, earliest due first.</item>
/// <item>The remaining loading length is filled with the longest combination of other orders that also
/// respects the payload (knapsack on 0.1 m steps, keeping the lightest combination for each length).</item>
/// <item>Among the vehicles, a full load using the most length wins; if none is full, the best fill rate wins.</item>
/// <item>Repeat until the route has no waiting order left. Loads below the threshold stay proposals that the
/// dispatcher can send with a recorded reason, or leave waiting for more material.</item>
/// </list>
/// </summary>
public static class LoadPlanner
{
    public static LoadPlan Plan(IEnumerable<TransportOrder> orders, IReadOnlyList<Vehicle> fleet, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if (fleet is null || fleet.Count == 0)
        {
            throw new ArgumentException("At least one vehicle is required.", nameof(fleet));
        }

        var waiting = orders.Where(o => o.Status == OrderStatus.Planned && o.TransportNumber is null).ToList();
        var unplannable = waiting
            .Where(o => !fleet.Any(v => o.LoadingLength <= v.LoadingLength && o.Weight <= v.Payload))
            .OrderBy(o => o.OrderNumber)
            .ToList();
        bool IsPressing(TransportOrder o) => o.Priority == OrderPriority.Urgent || o.IsOverdue(utcNow);

        var proposals = new List<LoadProposal>();
        foreach (var route in waiting.Except(unplannable)
                     .GroupBy(o => (o.SourceLocation, o.TargetLocation))
                     .OrderBy(g => g.Key.SourceLocation).ThenBy(g => g.Key.TargetLocation))
        {
            var remaining = route
                .OrderByDescending(IsPressing)
                .ThenBy(o => o.DueAt)
                .ThenByDescending(o => o.LoadingLength)
                .ToList();

            while (remaining.Count > 0)
            {
                LoadProposal? best = null;
                foreach (var vehicle in fleet.OrderBy(v => v.LoadingLength))
                {
                    var packed = Pack(remaining, vehicle, IsPressing);
                    if (packed.Count == 0)
                    {
                        continue;
                    }

                    var candidate = new LoadProposal(vehicle, route.Key.SourceLocation, route.Key.TargetLocation,
                        packed, packed.Any(IsPressing));
                    if (best is null || IsBetter(candidate, best))
                    {
                        best = candidate;
                    }
                }

                if (best is null)
                {
                    break;
                }

                proposals.Add(best);
                remaining.RemoveAll(o => best.Orders.Contains(o));
            }
        }

        return new LoadPlan(proposals, unplannable);
    }

    private static bool IsBetter(LoadProposal candidate, LoadProposal best) =>
        (candidate.IsFull, best.IsFull) switch
        {
            (true, false) => true,
            (false, true) => false,
            (true, true) => candidate.LoadedLength > best.LoadedLength,
            _ => candidate.FillRate > best.FillRate,
        };

    /// <summary>Pressing orders first (in order), then the longest subset of the others that still fits.</summary>
    private static List<TransportOrder> Pack(IReadOnlyList<TransportOrder> remaining, Vehicle vehicle, Func<TransportOrder, bool> isPressing)
    {
        var packed = new List<TransportOrder>();
        decimal length = 0, weight = 0;
        var pressing = remaining.Where(isPressing).ToList();
        foreach (var order in pressing)
        {
            if (length + order.LoadingLength <= vehicle.LoadingLength && weight + order.Weight <= vehicle.Payload)
            {
                packed.Add(order);
                length += order.LoadingLength;
                weight += order.Weight;
            }
        }

        // A vehicle that cannot take the most pressing order is not an option for this route.
        if (pressing.Count > 0 && !packed.Contains(pressing[0]))
        {
            return [];
        }

        packed.AddRange(LongestSubset(remaining.Where(o => !isPressing(o)).ToList(),
            vehicle.LoadingLength - length, vehicle.Payload - weight));
        return packed;
    }

    /// <summary>
    /// 0/1 knapsack on loading length in 0.1 m steps: for every reachable length keep the lightest combination,
    /// then take the longest length whose weight is within the payload.
    /// </summary>
    private static IEnumerable<TransportOrder> LongestSubset(IReadOnlyList<TransportOrder> candidates, decimal lengthLeft, decimal payloadLeft)
    {
        var limit = (int)Math.Floor(lengthLeft * 10);
        if (limit <= 0 || payloadLeft <= 0 || candidates.Count == 0)
        {
            return [];
        }

        var units = candidates.Select(o => (int)Math.Ceiling(o.LoadingLength * 10)).ToArray();
        var n = candidates.Count;
        var lightest = new decimal?[n + 1, limit + 1];
        lightest[0, 0] = 0;
        for (var i = 1; i <= n; i++)
        {
            for (var l = 0; l <= limit; l++)
            {
                var without = lightest[i - 1, l];
                decimal? with = l >= units[i - 1] && lightest[i - 1, l - units[i - 1]] is { } before
                    ? before + candidates[i - 1].Weight
                    : null;
                lightest[i, l] = (without, with) switch
                {
                    (null, _) => with,
                    (_, null) => without,
                    _ => Math.Min(without.Value, with.Value),
                };
            }
        }

        var best = limit;
        while (best > 0 && (lightest[n, best] is not { } w || w > payloadLeft))
        {
            best--;
        }

        var chosen = new List<TransportOrder>();
        for (var i = n; i > 0 && best > 0; i--)
        {
            if (lightest[i, best] != lightest[i - 1, best])
            {
                chosen.Add(candidates[i - 1]);
                best -= units[i - 1];
            }
        }

        return chosen;
    }
}
