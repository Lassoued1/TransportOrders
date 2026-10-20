using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed partial class EfOrderRepository
{
    public async Task StartPackingAsync(string basketNumber, Guid basketVersion, string user, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var basket = await context.Baskets.SingleAsync(b => b.Number == basketNumber, cancellationToken);
        CheckVersion(basket.Version, basketVersion);
        var equipment = await context.Equipment.SingleOrDefaultAsync(e => e.LocationId == basket.LocationId && e.Kind == EquipmentKind.Packing, cancellationToken)
            ?? throw new TransportRuleException("The loaded basket must be at a packing station.");
        if (await context.Packages.AnyAsync(p => p.SourceBasketNumber == basket.Number && p.ContentSequence == basket.ContentSequence, cancellationToken))
            throw new TransportRuleException("This basket load already has a package.");
        await CheckPackingOrdersAsync(context, basket, cancellationToken);
        var lot = await context.MaterialLots.SingleAsync(l => l.Number == basket.MaterialLotNumber, cancellationToken);
        var prefix = $"PK-{now.Year}-";
        var numbers = await context.Packages.Where(p => p.Number.StartsWith(prefix)).Select(p => p.Number).ToListAsync(cancellationToken);
        var sequence = numbers.Select(n => int.Parse(n[prefix.Length..], CultureInfo.InvariantCulture)).DefaultIfEmpty().Max() + 1;
        var package = new PackedPackage($"{prefix}{sequence:D6}", basket, lot, equipment.Id, user, now);
        equipment.Start(package.Number, user, now);
        context.Packages.Add(package);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompletePackingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var package = await context.Packages.SingleAsync(p => p.Number == packageNumber, cancellationToken);
        CheckVersion(package.Version, version);
        await CheckActivePackingAsync(context, package, cancellationToken);
        package.CompletePacking(user, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task InspectPackageAsync(string packageNumber, Guid version, bool visualPassed, bool quantityVerified,
        string user, string reason, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var package = await context.Packages.SingleAsync(p => p.Number == packageNumber, cancellationToken);
        CheckVersion(package.Version, version);
        var equipment = await CheckActivePackingAsync(context, package, cancellationToken);
        var basket = await context.Baskets.SingleAsync(b => b.Number == package.SourceBasketNumber, cancellationToken);
        await CheckPackingOrdersAsync(context, basket, cancellationToken);
        package.Inspect(visualPassed, quantityVerified, user, reason, now);
        if (package.Status == PackageStatus.Ready)
        {
            // Preserve old packing-to-shipping orders in the audit; shipping now belongs to the package.
            var legacy = await context.Orders.Include(o => o.History).Where(o => o.PieceNumber == basket.Number
                && o.Status == OrderStatus.Planned && o.SourceLocation == basket.LocationId && o.TargetLocation == ExtrusionPlant.Shipping).ToListAsync(cancellationToken);
            foreach (var order in legacy) order.TransitionTo(OrderStatus.Cancelled, user, now, $"Replaced by package {package.Number}");
            basket.Unload(package.Number, user, now);
            equipment.Finish(package.Number, user, now);
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task StartPackageShippingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var package = await context.Packages.SingleAsync(p => p.Number == packageNumber, cancellationToken);
        CheckVersion(package.Version, version);
        var source = await context.Equipment.SingleAsync(e => e.Id == package.EquipmentId, cancellationToken);
        if (!source.CanOperate || source.ActiveOperation is not null) throw new TransportRuleException("The packing station is unavailable.");
        if (package.LoadingLength > ExtrusionSimulator.Shuttle.LoadingLength || package.Weight > ExtrusionSimulator.Shuttle.Payload)
            throw new TransportRuleException("The package exceeds the demo transfer equipment capacity.");
        await CheckLocationRoomAsync(context, ExtrusionPlant.Shipping, cancellationToken);
        var shuttle = await GetFreeShuttleAsync(context, cancellationToken);
        package.Dispatch(ExtrusionPlant.Shipping, user, now);
        shuttle.Start(package.Number, user, now);
        context.PlcCommands.Add(new(PlcCommandKind.PackageTransfer, package.Number, source.LocationId!, ExtrusionPlant.Shipping,
            [new(package.Number, package.ContentSequence, package.Quantity, package.Weight, package.LoadingLength)], now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompletePackageShippingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var package = await context.Packages.SingleAsync(p => p.Number == packageNumber, cancellationToken);
        CheckVersion(package.Version, version);
        var shuttle = await context.Equipment.SingleAsync(e => e.Id == PlantEquipment.SharedShuttle, cancellationToken);
        package.Arrive(user, now);
        shuttle.Finish(package.Number, user, now);
        await MarkManualPlcArrivalAsync(context, PlcCommandKind.PackageTransfer, package.Number, user, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task StartEmptyReturnAsync(string basketNumber, Guid version, string target, string user, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var basket = await context.Baskets.SingleAsync(b => b.Number == basketNumber, cancellationToken);
        CheckVersion(basket.Version, version);
        var source = await context.Locations.SingleOrDefaultAsync(l => l.Id == basket.LocationId && l.Kind == StationKind.Packing, cancellationToken)
            ?? throw new TransportRuleException("Empty demo baskets return from packing stations.");
        var destination = await context.Locations.SingleAsync(l => l.Id == target, cancellationToken);
        if (destination.Kind != StationKind.Press) throw new TransportRuleException("An empty return must target a press outlet.");
        var equipment = await context.Equipment.Where(e => e.LocationId == source.Id || e.LocationId == target).ToListAsync(cancellationToken);
        if (equipment.Any(e => !e.CanOperate || e.ActiveOperation is not null)) throw new TransportRuleException("Return source or destination is unavailable.");
        if (basket.LoadingLength > ExtrusionSimulator.Shuttle.LoadingLength || basket.Weight > ExtrusionSimulator.Shuttle.Payload)
            throw new TransportRuleException("The empty basket exceeds transfer capacity.");
        await CheckLocationRoomAsync(context, target, cancellationToken);
        var shuttle = await GetFreeShuttleAsync(context, cancellationToken);
        var movement = new BasketReturn(basket, target, user, now);
        basket.DepartEmpty(movement, user, now);
        shuttle.Start($"RETURN:{movement.Id}", user, now);
        context.BasketReturns.Add(movement);
        context.PlcCommands.Add(new(PlcCommandKind.EmptyReturn, movement.Id.ToString(), movement.SourceLocation, movement.TargetLocation,
            [new(basket.Number, basket.ContentSequence, 0, basket.Weight, basket.LoadingLength)], now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task CompleteEmptyReturnAsync(Guid returnId, Guid version, string user, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var movement = await context.BasketReturns.SingleAsync(r => r.Id == returnId, cancellationToken);
        CheckVersion(movement.Version, version);
        var basket = await context.Baskets.SingleAsync(b => b.Number == movement.BasketNumber, cancellationToken);
        var target = await context.Equipment.SingleAsync(e => e.LocationId == movement.TargetLocation, cancellationToken);
        if (!target.CanOperate || target.ActiveOperation is not null) throw new TransportRuleException("The return destination is unavailable.");
        var shuttle = await context.Equipment.SingleAsync(e => e.Id == PlantEquipment.SharedShuttle, cancellationToken);
        movement.Complete(user, now);
        basket.ArriveEmpty(movement, user, now);
        shuttle.Finish($"RETURN:{movement.Id}", user, now);
        await MarkManualPlcArrivalAsync(context, PlcCommandKind.EmptyReturn, movement.Id.ToString(), user, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReloadBasketAsync(string basketNumber, Guid version, string lotNumber, int quantity, decimal weight,
        decimal loadingLength, string targetOven, string user, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var basket = await context.Baskets.SingleAsync(b => b.Number == basketNumber, cancellationToken);
        CheckVersion(basket.Version, version);
        var press = await context.Equipment.SingleOrDefaultAsync(e => e.LocationId == basket.LocationId && e.Kind == EquipmentKind.Press, cancellationToken)
            ?? throw new TransportRuleException("An empty basket must return to a press before reloading.");
        if (!press.CanOperate || press.ActiveOperation is not null) throw new TransportRuleException("The press outlet is unavailable.");
        var lot = await context.MaterialLots.SingleAsync(l => l.Number == lotNumber, cancellationToken);
        if (!await context.Locations.AnyAsync(l => l.Id == targetOven && l.Kind == StationKind.AgingOven, cancellationToken)
            || !(await context.Recipes.ToListAsync(cancellationToken)).Any(r => r.Supports(lot)))
            throw new TransportRuleException("Choose an oven and a material lot with an available demo recipe.");
        if (await context.Orders.AnyAsync(o => o.PieceNumber == basket.Number && (o.Status == OrderStatus.Planned || o.Status == OrderStatus.OnHold || o.Status == OrderStatus.InTransit), cancellationToken))
            throw new TransportRuleException("The basket still has an open material order.");
        basket.Reload(lot, quantity, weight, loadingLength, user, now);
        var prefix = $"TO-{now.Year}-";
        var numbers = await context.Orders.Where(o => o.OrderNumber.StartsWith(prefix)).Select(o => o.OrderNumber).ToListAsync(cancellationToken);
        var sequence = numbers.Select(n => int.Parse(n[prefix.Length..], CultureInfo.InvariantCulture)).DefaultIfEmpty().Max() + 1;
        context.Orders.Add(TransportOrder.ForBasket($"{prefix}{sequence:D4}", basket, lot, targetOven, OrderPriority.Normal, now.AddHours(1), now, user));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void CheckVersion(Guid actual, Guid expected)
    {
        if (actual != expected) throw new TransportRuleException("The plant state changed since it was loaded. Reload before trying again.");
    }
    private static Task<int> PackageSlotsAsync(AppDbContext context, string location, CancellationToken cancellationToken) =>
        context.Packages.CountAsync(p => p.ReservedLocationId == location || (p.LocationId == location
            && !context.Baskets.Any(b => b.Number == p.SourceBasketNumber && b.LocationId == location)), cancellationToken);
    private static async Task CheckLocationRoomAsync(AppDbContext context, string location, CancellationToken cancellationToken)
    {
        var destination = await context.Locations.SingleAsync(l => l.Id == location, cancellationToken);
        var used = await context.Baskets.CountAsync(b => b.LocationId == location || b.ReservedLocationId == location, cancellationToken)
            + await PackageSlotsAsync(context, location, cancellationToken);
        if (used >= destination.Capacity) throw new TransportRuleException($"Destination {location} has insufficient capacity.");
    }
    private static async Task<PlantEquipment> GetFreeShuttleAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var shuttle = await context.Equipment.SingleAsync(e => e.Id == PlantEquipment.SharedShuttle, cancellationToken);
        if (!shuttle.CanOperate || shuttle.ActiveOperation is not null) throw new TransportRuleException("Shared transfer equipment is unavailable.");
        return shuttle;
    }
    private static async Task CheckPackingOrdersAsync(AppDbContext context, Basket basket, CancellationToken cancellationToken)
    {
        if (await context.Orders.AnyAsync(o => o.PieceNumber == basket.Number && (o.Status == OrderStatus.OnHold || o.Status == OrderStatus.InTransit
            || (o.Status == OrderStatus.Planned && (o.SourceLocation != basket.LocationId || o.TargetLocation != ExtrusionPlant.Shipping))), cancellationToken))
            throw new TransportRuleException("Packing is blocked by a quality hold or another open material movement.");
    }
    private static async Task<PlantEquipment> CheckActivePackingAsync(AppDbContext context, PackedPackage package, CancellationToken cancellationToken)
    {
        var equipment = await context.Equipment.SingleAsync(e => e.Id == package.EquipmentId, cancellationToken);
        var basket = await context.Baskets.SingleAsync(b => b.Number == package.SourceBasketNumber, cancellationToken);
        if (!equipment.CanOperate || equipment.ActiveOperation != package.Number || basket.IsEmpty || basket.IsMoving
            || basket.ContentSequence != package.ContentSequence || basket.MaterialLotNumber != package.MaterialLotNumber
            || basket.LocationId != package.LocationId || basket.Quantity != package.Quantity || basket.Weight != package.Weight)
            throw new TransportRuleException("The packing equipment and physical basket load must match the package.");
        return equipment;
    }
}
