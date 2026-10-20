using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

/// <summary>One-time import for pre-model databases. Never recomputes positions on normal startup.</summary>
internal static class PlantModelInitializer
{
    internal static async Task InitializeAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Locations are created with the baskets in this transaction and mark the completed import.
        if (await context.Locations.AnyAsync(cancellationToken)) return;
        var orders = await context.Orders.AsNoTracking().ToListAsync(cancellationToken);
        var transports = await context.Transports.AsNoTracking().ToDictionaryAsync(t => t.TransportNumber, cancellationToken);
        var latest = orders.GroupBy(o => o.PieceNumber)
            .Select(g => g.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.OrderNumber, StringComparer.Ordinal).First()).ToList();
        var locations = ExtrusionPlant.Stations.Select(s => new PlantLocation(s.Id, s.Kind, s.Capacity)).ToList();
        // Preserve unfamiliar legacy station names; these buffers do not gain a fabricated process route.
        foreach (var id in orders.SelectMany(o => new[] { o.SourceLocation, o.TargetLocation }).Distinct())
            if (locations.All(l => l.Id != id)) locations.Add(new PlantLocation(id, StationKind.Buffer, Math.Max(1, latest.Count)));
        context.Locations.AddRange(locations);

        foreach (var group in latest.GroupBy(o => o.ParentPieceNumber ?? o.HeatNumber))
        {
            if (group.Select(o => (o.HeatNumber, o.Material)).Distinct().Count() != 1)
                throw new TransportRuleException($"Material lot {group.Key} has conflicting legacy contents; import requires review.");
            var first = group.First();
            context.MaterialLots.Add(new MaterialLot(group.Key, first.HeatNumber, first.Material));
        }
        foreach (var order in latest)
        {
            var transport = order.TransportNumber is { } number ? transports.GetValueOrDefault(number) : null;
            var arrived = order.Status == OrderStatus.Done || transport?.Status == TransportStatus.Done;
            var basket = new Basket(order.PieceNumber, order.ParentPieceNumber ?? order.HeatNumber,
                order.Quantity, order.Weight, order.LoadingLength, arrived ? order.TargetLocation : order.SourceLocation);
            if (transport?.Status == TransportStatus.InTransit)
                basket.Depart(order.SourceLocation, order.TargetLocation, transport.TransportNumber);
            context.Baskets.Add(basket);
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
