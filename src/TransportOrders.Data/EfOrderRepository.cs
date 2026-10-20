using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed partial class EfOrderRepository(IDbContextFactory<AppDbContext> contextFactory) : IOrderRepository, IPlcRepository
{
    public async Task<PlantState> GetPlantStateAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var baskets = await context.Baskets.AsNoTracking().Include(b => b.History).OrderBy(b => b.Number).ToListAsync(cancellationToken);
        var lots = await context.MaterialLots.AsNoTracking().ToListAsync(cancellationToken);
        var locations = await context.Locations.AsNoTracking().OrderBy(l => l.Id).ToListAsync(cancellationToken);
        var orders = await WithHistory(context).OrderBy(o => o.OrderNumber).ToListAsync(cancellationToken);
        var transports = await context.Transports.AsNoTracking().OrderByDescending(t => t.TransportNumber).ToListAsync(cancellationToken);
        var equipment = await context.Equipment.AsNoTracking().Include(e => e.History).ToListAsync(cancellationToken);
        var recipes = await context.Recipes.AsNoTracking().Include(r => r.History).ToListAsync(cancellationToken);
        var cycles = await context.OvenCycles.AsNoTracking().Include(c => c.Load).ToListAsync(cancellationToken);
        var packages = await context.Packages.AsNoTracking().Include(p => p.History).ToListAsync(cancellationToken);
        var returns = await context.BasketReturns.AsNoTracking().ToListAsync(cancellationToken);
        var commands = await context.PlcCommands.AsNoTracking().Include(c => c.History).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var plcSimulation = await context.PlcSimulation.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(baskets, lots, locations, orders, transports) { Equipment = equipment, Recipes = recipes, Cycles = cycles, Packages = packages, Returns = returns, PlcCommands = commands, PlcSimulation = plcSimulation };
    }

    public async Task<IReadOnlyList<TransportOrder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await WithHistory(context)
            .OrderBy(o => o.OrderNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<TransportOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await WithHistory(context).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<TransportOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderNumber);

        // Order numbers are stored upper-case, so normalising the input gives a case-insensitive match.
        var normalized = orderNumber.Trim().ToUpperInvariant();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await WithHistory(context).FirstOrDefaultAsync(o => o.OrderNumber == normalized, cancellationToken);
    }

    public async Task SaveAsync(TransportOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var basket = await context.Baskets.SingleOrDefaultAsync(b => b.Number == order.PieceNumber, cancellationToken)
            ?? throw new TransportRuleException($"Basket {order.PieceNumber} is not registered.");
        await ValidateContentsAsync(context, basket, order, cancellationToken);
        if (order.Id == 0)
        {
            if (basket.IsMoving || basket.LocationId != order.SourceLocation || order.Status != OrderStatus.Planned)
                throw new TransportRuleException("A new transport order must start at the stationary basket's actual location.");
            if (!await context.Locations.AnyAsync(l => l.Id == order.TargetLocation, cancellationToken))
                throw new TransportRuleException($"Unknown destination {order.TargetLocation}.");
            if (await context.Orders.AnyAsync(o => o.PieceNumber == basket.Number
                && (o.Status == OrderStatus.Planned || o.Status == OrderStatus.OnHold || o.Status == OrderStatus.InTransit), cancellationToken))
                throw new TransportRuleException($"Basket {basket.Number} already has an open transport order.");
        }
        else
        {
            var stored = await WithHistory(context).SingleAsync(o => o.Id == order.Id, cancellationToken);
            EnsureFreshHistory(stored, order);
            if (basket.IsMoving)
                throw new TransportRuleException("An active basket transfer must be changed through its transport.");
            if (stored.TransportNumber != order.TransportNumber)
                throw new TransportRuleException("Transport membership must be saved through the transport.");
        }
        context.Orders.Update(order);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transport>> GetTransportsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Transports
            .AsNoTracking()
            .OrderByDescending(t => t.TransportNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<string> NextTransportNumberAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var prefix = $"TR-{utcNow.Year}-";
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var numbers = await context.Transports
            .Where(t => t.TransportNumber.StartsWith(prefix))
            .Select(t => t.TransportNumber)
            .ToListAsync(cancellationToken);
        var last = numbers.Select(n => int.Parse(n[prefix.Length..], CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        return $"{prefix}{last + 1:D4}";
    }

    public async Task SaveTransportAsync(Transport transport, IReadOnlyCollection<TransportOrder> orders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(orders);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (orders.Count == 0 || orders.Count != transport.OrderCount
            || orders.Select(o => o.PieceNumber).Distinct().Count() != orders.Count
            || orders.Any(o => o.TransportNumber != transport.TransportNumber))
            throw new TransportRuleException("Pass the complete, distinct basket load of this transport.");
        var numbers = orders.Select(o => o.PieceNumber).ToList();
        var baskets = await context.Baskets.Where(b => numbers.Contains(b.Number)).ToDictionaryAsync(b => b.Number, cancellationToken);
        if (baskets.Count != orders.Count) throw new TransportRuleException("Every transported basket must be registered.");
        var existingTransport = await context.Transports.AsNoTracking().SingleOrDefaultAsync(t => t.TransportNumber == transport.TransportNumber, cancellationToken);
        var dispatching = transport.Id == 0;
        if (dispatching)
        {
            if (existingTransport is not null || transport.Status != TransportStatus.InTransit)
                throw new TransportRuleException("This transport number is already used or the new transfer is invalid.");
            if (await context.Baskets.AnyAsync(b => b.ActiveTransportNumber != null, cancellationToken))
                throw new TransportRuleException("The shared transfer equipment is already occupied.");
            var destination = await context.Locations.SingleOrDefaultAsync(l => l.Id == transport.TargetLocation, cancellationToken)
                ?? throw new TransportRuleException($"Unknown destination {transport.TargetLocation}.");
            var used = await context.Baskets.CountAsync(b => b.LocationId == destination.Id || b.ReservedLocationId == destination.Id, cancellationToken);
            used += await PackageSlotsAsync(context, destination.Id, cancellationToken);
            if (used + orders.Count > destination.Capacity)
                throw new TransportRuleException($"Destination {destination.Id} has insufficient basket capacity.");
            await ValidateEquipmentTransferAsync(context, transport, baskets.Values.ToList(), cancellationToken);
        }
        else
        {
            if (existingTransport?.Status != TransportStatus.InTransit || transport.Status != TransportStatus.Done)
                throw new TransportRuleException("Only the arrival of an active transport can be saved.");
            var storedNumbers = await context.Orders.Where(o => o.TransportNumber == transport.TransportNumber)
                .Select(o => o.OrderNumber).ToListAsync(cancellationToken);
            if (!storedNumbers.Order().SequenceEqual(orders.Select(o => o.OrderNumber).Order()))
                throw new TransportRuleException("The arrival must contain every order on the transport.");
            await ValidateArrivalEquipmentAsync(context, transport, cancellationToken);
        }
        foreach (var order in orders)
        {
            var stored = await WithHistory(context).SingleAsync(o => o.Id == order.Id, cancellationToken);
            EnsureFreshHistory(stored, order);
            var basket = baskets[order.PieceNumber];
            await ValidateContentsAsync(context, basket, order, cancellationToken);
            if (dispatching)
            {
                if (stored.Status != OrderStatus.Planned || stored.TransportNumber is not null || order.Status != OrderStatus.InTransit)
                    throw new TransportRuleException($"Order {order.OrderNumber} is no longer available to dispatch.");
                basket.Depart(order.SourceLocation, order.TargetLocation, transport.TransportNumber);
            }
            else
            {
                if (order.Status is not (OrderStatus.Done or OrderStatus.Cancelled))
                    throw new TransportRuleException("An arrived order must be complete or previously cancelled.");
                basket.Arrive(transport.TransportNumber);
            }
        }
        context.Transports.Update(transport);
        context.Orders.UpdateRange(orders);
        if (dispatching)
            context.PlcCommands.Add(new(PlcCommandKind.MaterialTransfer, transport.TransportNumber, transport.SourceLocation, transport.TargetLocation,
                baskets.Values.Select(b => new PlcLoadItem(b.Number, b.ContentSequence, b.Quantity, b.Weight, b.LoadingLength)), transport.DispatchedAt));
        else await MarkManualPlcArrivalAsync(context, PlcCommandKind.MaterialTransfer, transport.TransportNumber,
            orders.First().History[^1].ChangedBy, transport.CompletedAt!.Value, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void EnsureFreshHistory(TransportOrder stored, TransportOrder changed)
    {
        if (!stored.History.Select(h => h.Id).Order().SequenceEqual(changed.History.Where(h => h.Id != 0).Select(h => h.Id).Order()))
            throw new TransportRuleException($"Order {changed.OrderNumber} changed since it was loaded. Reload before trying again.");
    }

    private static async Task ValidateContentsAsync(AppDbContext context, Basket basket, TransportOrder order, CancellationToken cancellationToken)
    {
        var lot = await context.MaterialLots.SingleAsync(l => l.Number == basket.MaterialLotNumber, cancellationToken);
        if (basket.IsEmpty || order.BasketContentSequence != basket.ContentSequence
            || order.Quantity != basket.Quantity || order.Weight != basket.Weight || order.LoadingLength != basket.LoadingLength
            || order.HeatNumber != lot.BatchNumber || order.Material != lot.Material
            || (order.ParentPieceNumber ?? order.HeatNumber) != lot.Number)
            throw new TransportRuleException($"Order {order.OrderNumber} does not match basket {basket.Number}'s material contents.");
    }

    private static IQueryable<TransportOrder> WithHistory(AppDbContext context) =>
        context.Orders
            .AsNoTracking()
            .Include(o => o.History.OrderBy(h => h.ChangedAt).ThenBy(h => h.Id));
}
