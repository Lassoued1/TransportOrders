using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

internal static class PlcModelInitializer
{
    internal static async Task InitializeAsync(AppDbContext context, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await context.PlcSimulation.AnyAsync(cancellationToken))
        {
            var settings = new PlcSimulationSettings(); settings.Set(true, false, "INITIALIZER", "Demo connection initialized", now);
            context.PlcSimulation.Add(settings);
        }
        var known = await context.PlcCommands.Select(c => new { c.Kind, c.Operation }).ToListAsync(cancellationToken);
        foreach (var transport in await context.Transports.Where(t => t.Status == TransportStatus.InTransit).ToListAsync(cancellationToken))
        {
            if (known.Any(c => c.Kind == PlcCommandKind.MaterialTransfer && c.Operation == transport.TransportNumber)) continue;
            var orders = await context.Orders.Where(o => o.TransportNumber == transport.TransportNumber).ToListAsync(cancellationToken);
            context.PlcCommands.Add(new(PlcCommandKind.MaterialTransfer, transport.TransportNumber, transport.SourceLocation, transport.TargetLocation,
                orders.Select(o => new PlcLoadItem(o.PieceNumber, o.BasketContentSequence, o.Quantity, o.Weight, o.LoadingLength)), now));
        }
        foreach (var package in await context.Packages.Where(p => p.Status == PackageStatus.InTransit).ToListAsync(cancellationToken))
        {
            if (known.Any(c => c.Kind == PlcCommandKind.PackageTransfer && c.Operation == package.Number)) continue;
            var source = await context.Equipment.SingleAsync(e => e.Id == package.EquipmentId, cancellationToken);
            context.PlcCommands.Add(new(PlcCommandKind.PackageTransfer, package.Number, source.LocationId!, package.ReservedLocationId!,
                [new(package.Number, package.ContentSequence, package.Quantity, package.Weight, package.LoadingLength)], now));
        }
        foreach (var movement in await context.BasketReturns.Where(r => r.ArrivedAt == null).ToListAsync(cancellationToken))
        {
            if (known.Any(c => c.Kind == PlcCommandKind.EmptyReturn && c.Operation == movement.Id.ToString())) continue;
            var basket = await context.Baskets.SingleAsync(b => b.Number == movement.BasketNumber, cancellationToken);
            context.PlcCommands.Add(new(PlcCommandKind.EmptyReturn, movement.Id.ToString(), movement.SourceLocation, movement.TargetLocation,
                [new(basket.Number, basket.ContentSequence, 0, basket.Weight, basket.LoadingLength)], now));
        }
        await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }
}
