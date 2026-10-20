using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed partial class EfOrderRepository
{
    public async Task SetEquipmentControlAsync(string equipmentId, Guid expectedVersion, EquipmentMode mode,
        EquipmentCondition condition, string user, string reason, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var equipment = await context.Equipment.Include(e => e.History).SingleAsync(e => e.Id == equipmentId, cancellationToken);
        if (equipment.Version != expectedVersion) throw new TransportRuleException("Equipment changed since it was loaded. Reload before trying again.");
        equipment.SetControl(mode, condition, user, reason, now);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task StartOvenCycleAsync(string equipmentId, string recipeId, IReadOnlyCollection<string> basketNumbers,
        string user, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var equipment = await context.Equipment.SingleAsync(e => e.Id == equipmentId, cancellationToken);
        if (equipment.Kind != EquipmentKind.Oven || !equipment.CanSimulate || equipment.ActiveOperation is not null)
            throw new TransportRuleException("The oven must be available and in automatic mode to start a simulated cycle.");
        var recipe = await context.Recipes.SingleAsync(r => r.Id == recipeId, cancellationToken);
        var baskets = await context.Baskets.Where(b => b.LocationId == equipment.LocationId).ToListAsync(cancellationToken);
        if (baskets.Any(b => b.IsEmpty)) throw new TransportRuleException("Empty baskets cannot enter a treatment cycle.");
        if (basketNumbers.Count != baskets.Count || !basketNumbers.Order().SequenceEqual(baskets.Select(b => b.Number).Order()))
            throw new TransportRuleException("A cycle must include exactly the complete physical oven load.");
        if (await context.Baskets.AnyAsync(b => b.ReservedLocationId == equipment.LocationId, cancellationToken))
            throw new TransportRuleException("An incoming oven transfer must arrive before the cycle starts.");
        var numbers = baskets.Select(b => b.Number).ToList();
        if (await context.Orders.AnyAsync(o => numbers.Contains(o.PieceNumber)
            && (o.Status == OrderStatus.OnHold || o.Status == OrderStatus.InTransit
                || (o.Status == OrderStatus.Planned && o.SourceLocation != equipment.LocationId)), cancellationToken))
            throw new TransportRuleException("Baskets with incoming or quality-held orders cannot enter an oven cycle.");
        var cycles = await context.OvenCycles.Include(c => c.Load).Where(c => c.EquipmentId == equipmentId).ToListAsync(cancellationToken);
        if (cycles.Any(c => c.Phase != OvenPhase.Completed || c.Load.Any(l => baskets.Any(b => b.Number == l.BasketNumber && b.ContentSequence == l.ContentSequence))))
            throw new TransportRuleException("This oven load already has a treatment cycle.");
        var lotNumbers = baskets.Select(b => b.MaterialLotNumber).ToList();
        var lots = await context.MaterialLots.Where(l => lotNumbers.Contains(l.Number)).ToListAsync(cancellationToken);
        var cycle = new OvenCycle(equipmentId, recipe, baskets, lots, now);
        equipment.Start(cycle.Id.ToString(), user, now);
        context.OvenCycles.Add(cycle);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AdvanceOvenCycleAsync(Guid cycleId, Guid expectedVersion, int minutes, string user, DateTime now,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var cycle = await context.OvenCycles.Include(c => c.Load).SingleAsync(c => c.Id == cycleId, cancellationToken);
        if (cycle.Version != expectedVersion) throw new TransportRuleException("The oven cycle changed since it was loaded.");
        var equipment = await context.Equipment.SingleAsync(e => e.Id == cycle.EquipmentId, cancellationToken);
        if (!equipment.CanSimulate || equipment.ActiveOperation != cycle.Id.ToString())
            throw new TransportRuleException("A fault, maintenance or manual mode suspends simulated oven time.");
        var numbers = cycle.Load.Select(l => l.BasketNumber).ToList();
        if (await context.Baskets.CountAsync(b => numbers.Contains(b.Number) && b.LocationId == equipment.LocationId, cancellationToken) != numbers.Count)
            throw new TransportRuleException("The oven's physical load no longer matches the cycle.");
        cycle.Advance(minutes, now);
        equipment.RecordCycleProgress(cycle, user, now);
        if (cycle.Phase == OvenPhase.Completed) equipment.Finish(cycle.Id.ToString(), user, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ValidateEquipmentTransferAsync(AppDbContext context, Transport transport,
        IReadOnlyCollection<Basket> load, CancellationToken cancellationToken)
    {
        var equipment = await context.Equipment.Where(e => e.Id == PlantEquipment.SharedShuttle
            || e.LocationId == transport.SourceLocation || e.LocationId == transport.TargetLocation).ToListAsync(cancellationToken);
        if (equipment.Any(e => !e.CanOperate || e.ActiveOperation is not null))
            throw new TransportRuleException("Source, destination or transfer equipment is unavailable.");
        if (equipment.Any(e => e.Kind == EquipmentKind.Packing && e.LocationId == transport.SourceLocation))
            throw new TransportRuleException("Packing outputs move as checked packages; baskets return empty through their return operation.");
        var cycles = await context.OvenCycles.Include(c => c.Load).ToListAsync(cancellationToken);
        var sourceOven = equipment.SingleOrDefault(e => e.Kind == EquipmentKind.Oven && e.LocationId == transport.SourceLocation);
        if (sourceOven is not null && load.Any(b => !cycles.Any(c => c.EquipmentId == sourceOven.Id
            && c.Phase == OvenPhase.Completed && c.Load.Any(l => l.BasketNumber == b.Number && l.MaterialLotNumber == b.MaterialLotNumber && l.ContentSequence == b.ContentSequence))))
            throw new TransportRuleException("Oven baskets cannot leave before their treatment and cooling are complete.");
        var targetOven = equipment.SingleOrDefault(e => e.Kind == EquipmentKind.Oven && e.LocationId == transport.TargetLocation);
        if (targetOven is not null)
        {
            var present = await context.Baskets.Where(b => b.LocationId == targetOven.LocationId || b.ReservedLocationId == targetOven.LocationId).ToListAsync(cancellationToken);
            if (present.Any(b => cycles.Any(c => c.EquipmentId == targetOven.Id && c.Phase == OvenPhase.Completed && c.Load.Any(l => l.BasketNumber == b.Number && l.ContentSequence == b.ContentSequence))))
                throw new TransportRuleException("Empty the completed oven batch before admitting a new batch.");
            var lots = await context.MaterialLots.ToDictionaryAsync(l => l.Number, cancellationToken);
            var recipes = await context.Recipes.ToListAsync(cancellationToken);
            if (!recipes.Any(r => present.Concat(load).All(b => r.Supports(lots[b.MaterialLotNumber]))))
                throw new TransportRuleException("The oven load has no common compatible recipe.");
        }
        equipment.Single(e => e.Id == PlantEquipment.SharedShuttle).Start(transport.TransportNumber, transport.DispatchedBy, transport.DispatchedAt);
    }

    private static async Task ValidateArrivalEquipmentAsync(AppDbContext context, Transport transport, CancellationToken cancellationToken)
    {
        var equipment = await context.Equipment.Where(e => e.Id == PlantEquipment.SharedShuttle || e.LocationId == transport.TargetLocation).ToListAsync(cancellationToken);
        if (equipment.Any(e => !e.CanOperate || (e.Id != PlantEquipment.SharedShuttle && e.ActiveOperation is not null)))
            throw new TransportRuleException("Arrival is suspended while destination or transfer equipment is unavailable.");
        equipment.Single(e => e.Id == PlantEquipment.SharedShuttle).Finish(transport.TransportNumber,
            "TRANSFER", transport.CompletedAt ?? throw new TransportRuleException("Arrival needs a completion time."));
    }
}
