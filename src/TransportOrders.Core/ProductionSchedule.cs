namespace TransportOrders.Core;

public enum ScheduledActionKind
{
    ArriveTransport, ArriveReturn, ArrivePackage, CompletePacking, InspectPackage, StartPacking,
    ReturnEmpty, ShipPackage, CreatePackingOrder, DispatchBasket, StartOvenCycle, AdvanceOvenCycle,
}
public enum ScheduleReason
{
    ActiveTransfer, FreePackingStation, InspectionRequired, PackingRequired, ReturnEmpty, ShipCheckedPackage,
    TreatmentComplete, CompatibleBatch, FullBatch, UrgentPartialBatch, NoCompatibleIncoming, ContinueTreatment,
    QualityHold, Cancelled, EquipmentFault, EquipmentManual, EquipmentBusy, DestinationFull,
    IncompatibleRecipe, TreatmentPending, AwaitingBatch, ShippingOrderReplaced, Oversize, NoDestination,
}
public sealed record ScheduledAction(ScheduledActionKind Kind, string Reference, string Resource,
    string? Source, string? Target, string? RecipeId, IReadOnlyList<string> BasketNumbers,
    Guid ExpectedVersion, int Rank, bool Urgent, DateTime? DueAt, ScheduleReason Reason);
public sealed record ScheduleBlock(string Reference, string Resource, ScheduleReason Reason);
public sealed record ProductionSchedule(DateTime GeneratedAt, IReadOnlyList<ScheduledAction> Actions, IReadOnlyList<ScheduleBlock> Blocked)
{
    public ScheduledAction? Next => Actions.FirstOrDefault();
}

/// <summary>A deterministic, read-only one-event plan. Repository rules remain authoritative at execution.</summary>
public static class ProductionScheduler
{
    public static ProductionSchedule Plan(PlantState state, DateTime now)
    {
        var actions = new List<ScheduledAction>();
        var blocked = new List<ScheduleBlock>();
        var positions = state.Locate();
        var shuttle = state.Equipment.FirstOrDefault(e => e.Id == PlantEquipment.SharedShuttle);
        // Demo run has one global automatic control; faults/manual mode pause it as before.
        var globalBlock = shuttle is { CanOperate: false } ? ScheduleReason.EquipmentFault
            : shuttle is { CanSimulate: false } ? ScheduleReason.EquipmentManual : (ScheduleReason?)null;
        bool Available(string reference, string location, bool allowActive = false)
        {
            var equipment = state.Equipment.FirstOrDefault(e => e.LocationId == location);
            var reason = equipment is { CanOperate: false } ? ScheduleReason.EquipmentFault
                : equipment is { CanSimulate: false } ? ScheduleReason.EquipmentManual
                : !allowActive && equipment?.ActiveOperation is not null ? ScheduleReason.EquipmentBusy : (ScheduleReason?)null;
            if (reason is null) return true;
            blocked.Add(new(reference, equipment!.Id, reason.Value)); return false;
        }
        void Add(ScheduledActionKind kind, string reference, string resource, int rank, ScheduleReason reason,
            string? source = null, string? target = null, Guid version = default, bool urgent = false,
            DateTime? due = null, string? recipe = null, IReadOnlyList<string>? baskets = null)
        {
            actions.Add(new(kind, reference, resource, source, target, recipe, baskets ?? [], version, rank, urgent, due, reason));
        }
        bool SharedFree(string reference)
        {
            if (shuttle?.ActiveOperation is null && !state.Baskets.Any(b => b.IsMoving)
                && !state.Packages.Any(p => p.Status == PackageStatus.InTransit)) return true;
            blocked.Add(new(reference, PlantEquipment.SharedShuttle, ScheduleReason.EquipmentBusy)); return false;
        }
        foreach (var transfer in state.Transports.Where(t => t.Status == TransportStatus.InTransit))
            if (Available(transfer.TransportNumber, transfer.TargetLocation))
                Add(ScheduledActionKind.ArriveTransport, transfer.TransportNumber, PlantEquipment.SharedShuttle, 0,
                    ScheduleReason.ActiveTransfer, transfer.SourceLocation, transfer.TargetLocation);
        foreach (var returning in state.Returns.Where(r => r.ArrivedAt is null))
            if (Available(returning.BasketNumber, returning.TargetLocation))
                Add(ScheduledActionKind.ArriveReturn, returning.Id.ToString(), PlantEquipment.SharedShuttle, 0,
                    ScheduleReason.ActiveTransfer, returning.SourceLocation, returning.TargetLocation, returning.Version, baskets: [returning.BasketNumber]);
        foreach (var package in state.Packages)
        {
            var equipment = state.Equipment.FirstOrDefault(e => e.Id == package.EquipmentId);
            if (package.Status == PackageStatus.InTransit)
                Add(ScheduledActionKind.ArrivePackage, package.Number, PlantEquipment.SharedShuttle, 0,
                    ScheduleReason.ActiveTransfer, target: package.ReservedLocationId, version: package.Version);
            else if (package.Status == PackageStatus.Rejected)
                blocked.Add(new(package.Number, package.EquipmentId, ScheduleReason.QualityHold));
            else if (package.Status is PackageStatus.Packing or PackageStatus.AwaitingInspection)
            {
                if (!Available(package.Number, equipment?.LocationId ?? "", allowActive: true)) continue;
                if (state.Orders.Any(o => o.PieceNumber == package.SourceBasketNumber && o.BasketContentSequence == package.ContentSequence && o.Status == OrderStatus.OnHold))
                { blocked.Add(new(package.Number, package.EquipmentId, ScheduleReason.QualityHold)); continue; }
                Add(package.Status == PackageStatus.Packing ? ScheduledActionKind.CompletePacking : ScheduledActionKind.InspectPackage,
                    package.Number, package.EquipmentId, 10, package.Status == PackageStatus.Packing ? ScheduleReason.FreePackingStation : ScheduleReason.InspectionRequired,
                    package.LocationId, version: package.Version);
            }
            else if (package.Status == PackageStatus.Ready)
            {
                if (!Available(package.Number, equipment?.LocationId ?? "") || !SharedFree(package.Number)) continue;
                if (!state.HasRoom(ExtrusionPlant.Shipping, positions))
                { blocked.Add(new(package.Number, ExtrusionPlant.Shipping, ScheduleReason.DestinationFull)); continue; }
                if (package.LoadingLength > ExtrusionSimulator.Shuttle.LoadingLength || package.Weight > ExtrusionSimulator.Shuttle.Payload)
                { blocked.Add(new(package.Number, PlantEquipment.SharedShuttle, ScheduleReason.Oversize)); continue; }
                Add(ScheduledActionKind.ShipPackage, package.Number, PlantEquipment.SharedShuttle, 21,
                    ScheduleReason.ShipCheckedPackage, package.LocationId, ExtrusionPlant.Shipping, package.Version);
            }
        }

        var incoming = new List<BasketPosition>();
        foreach (var basket in positions.Where(b => !b.IsMoving))
        {
            var order = basket.Order;
            var location = state.Locations.FirstOrDefault(l => l.Id == basket.Location);
            if (basket.Basket.IsEmpty)
            {
                if (location?.Kind != StationKind.Packing || !Available(basket.BasketNumber, location.Id) || !SharedFree(basket.BasketNumber)) continue;
                var target = state.Equipment.Where(e => e.Kind == EquipmentKind.Press && e.CanSimulate && e.ActiveOperation is null
                    && state.HasRoom(e.LocationId!, positions)).OrderBy(e => state.Occupied(e.LocationId!) + state.Reserved(e.LocationId!)).ThenBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault();
                if (target is null) { blocked.Add(new(basket.BasketNumber, "PRESS", ScheduleReason.NoDestination)); continue; }
                Add(ScheduledActionKind.ReturnEmpty, basket.BasketNumber, PlantEquipment.SharedShuttle, 20,
                    ScheduleReason.ReturnEmpty, location.Id, target.LocationId, basket.Basket.Version, baskets: [basket.BasketNumber]);
                continue;
            }
            if (order?.Status is OrderStatus.OnHold or OrderStatus.Cancelled)
            { blocked.Add(new(basket.BasketNumber, location?.Id ?? "", order.Status == OrderStatus.OnHold ? ScheduleReason.QualityHold : ScheduleReason.Cancelled)); continue; }
            var urgent = order?.Priority == OrderPriority.Urgent;
            if (location?.Kind == StationKind.Packing)
            {
                if (state.Packages.Any(p => p.SourceBasketNumber == basket.BasketNumber && p.ContentSequence == basket.Basket.ContentSequence)) continue;
                if (order?.Status == OrderStatus.Planned && order.TargetLocation != ExtrusionPlant.Shipping)
                { blocked.Add(new(basket.BasketNumber, location.Id, ScheduleReason.ShippingOrderReplaced)); continue; }
                if (Available(basket.BasketNumber, location.Id))
                    Add(ScheduledActionKind.StartPacking, basket.BasketNumber, location.Id, 15, ScheduleReason.PackingRequired,
                        location.Id, version: basket.Basket.Version, urgent: urgent, due: order?.DueAt, baskets: [basket.BasketNumber]);
                continue;
            }
            if (location?.Kind == StationKind.AgingOven && order?.Status == OrderStatus.Done && state.HasCompletedTreatment(basket.Basket, location.Id))
            {
                if (!Available(basket.BasketNumber, location.Id)) continue;
                var packing = state.Equipment.Where(e => e.Kind == EquipmentKind.Packing && e.CanSimulate && e.ActiveOperation is null && state.HasRoom(e.LocationId!, positions))
                    .OrderBy(e => state.Orders.Count(o => o.IsOpen && o.TargetLocation == e.LocationId)).ThenBy(e => e.Id, StringComparer.Ordinal).FirstOrDefault();
                if (packing is null) { blocked.Add(new(basket.BasketNumber, "PACK", ScheduleReason.DestinationFull)); continue; }
                Add(ScheduledActionKind.CreatePackingOrder, basket.BasketNumber, location.Id, 30, ScheduleReason.TreatmentComplete,
                    location.Id, packing.LocationId, basket.Basket.Version, urgent, order.DueAt, baskets: [basket.BasketNumber]);
                continue;
            }
            if (order?.Status != OrderStatus.Planned) continue;
            if (!Available(basket.BasketNumber, order.SourceLocation) || !Available(basket.BasketNumber, order.TargetLocation) || !SharedFree(basket.BasketNumber)) continue;
            if (!state.HasRoom(order.TargetLocation, positions))
            { blocked.Add(new(basket.BasketNumber, order.TargetLocation, ScheduleReason.DestinationFull)); continue; }
            if (location?.Kind == StationKind.AgingOven && !state.HasCompletedTreatment(basket.Basket, location.Id))
            { blocked.Add(new(basket.BasketNumber, location.Id, ScheduleReason.TreatmentPending)); continue; }
            if (!state.CanTransfer(basket, automatic: true))
            { blocked.Add(new(basket.BasketNumber, order.TargetLocation, ScheduleReason.IncompatibleRecipe)); continue; }
            if (basket.Basket.LoadingLength > ExtrusionSimulator.Shuttle.LoadingLength || basket.Basket.Weight > ExtrusionSimulator.Shuttle.Payload)
            { blocked.Add(new(basket.BasketNumber, PlantEquipment.SharedShuttle, ScheduleReason.Oversize)); continue; }
            if (state.Locations.Any(l => l.Id == order.TargetLocation && l.Kind == StationKind.AgingOven)) incoming.Add(basket);
            else Add(ScheduledActionKind.DispatchBasket, basket.BasketNumber, PlantEquipment.SharedShuttle, 40,
                ScheduleReason.TreatmentComplete, order.SourceLocation, order.TargetLocation, basket.Basket.Version, urgent, order.DueAt, baskets: [basket.BasketNumber]);
        }
        // Choose one alloy family per oven, keeping compatible followers ahead of competing families.
        foreach (var oven in incoming.GroupBy(b => b.Order!.TargetLocation))
        {
            var groups = oven.GroupBy(b => state.Recipes.OrderBy(r => r.Id, StringComparer.Ordinal).First(r => r.Supports(b.MaterialLot)).Id)
                .OrderByDescending(g => g.Any(b => b.Order!.Priority == OrderPriority.Urgent))
                .ThenBy(g => g.Min(b => b.Order!.DueAt)).ThenByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            foreach (var group in groups)
                foreach (var basket in group)
                    if (group.Key == groups[0].Key)
                        Add(ScheduledActionKind.DispatchBasket, basket.BasketNumber, PlantEquipment.SharedShuttle, 50,
                            ScheduleReason.CompatibleBatch, basket.Order!.SourceLocation, oven.Key, basket.Basket.Version,
                            basket.Order.Priority == OrderPriority.Urgent, basket.Order.DueAt, group.Key, [basket.BasketNumber]);
                    else blocked.Add(new(basket.BasketNumber, oven.Key, ScheduleReason.AwaitingBatch));
        }
        foreach (var oven in state.Equipment.Where(e => e.Kind == EquipmentKind.Oven).OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            var cycle = state.Cycles.FirstOrDefault(c => c.EquipmentId == oven.Id && c.Phase != OvenPhase.Completed);
            if (cycle is not null)
            {
                if (!Available(cycle.Id.ToString(), oven.LocationId!, allowActive: true)) continue;
                // Fair progress between active cycles; even a fixed test clock cannot starve the second oven.
                Add(ScheduledActionKind.AdvanceOvenCycle, cycle.Id.ToString(), oven.Id, 70 + Math.Min(cycle.ElapsedMinutes, 100000),
                    ScheduleReason.ContinueTreatment, oven.LocationId, version: cycle.Version, recipe: cycle.RecipeId,
                    baskets: cycle.Load.Select(l => l.BasketNumber).ToList());
                continue;
            }
            var load = positions.Where(b => b.Location == oven.LocationId && !b.Basket.IsEmpty).ToList();
            if (load.Count == 0 || load.Any(b => state.HasCompletedTreatment(b.Basket, oven.Id))) continue;
            if (load.Any(b => b.Order?.Status is OrderStatus.OnHold or OrderStatus.Cancelled or OrderStatus.InTransit)) continue;
            if (!Available(oven.Id, oven.LocationId!) || state.Reserved(oven.LocationId!) > 0) continue;
            var recipe = state.Recipes.OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault(r => load.All(b => r.Supports(b.MaterialLot)));
            if (recipe is null) { blocked.Add(new(oven.Id, oven.Id, ScheduleReason.IncompatibleRecipe)); continue; }
            var full = load.Count >= state.Locations.Single(l => l.Id == oven.LocationId).Capacity;
            var urgent = load.Any(b => b.Order?.Priority == OrderPriority.Urgent || b.Order?.IsOverdue(now) == true);
            if (!full && !urgent && incoming.Any(b => b.Order!.TargetLocation == oven.LocationId && recipe.Supports(b.MaterialLot)))
            { blocked.Add(new(oven.Id, oven.Id, ScheduleReason.AwaitingBatch)); continue; }
            Add(ScheduledActionKind.StartOvenCycle, oven.Id, oven.Id, urgent ? 45 : 60,
                full ? ScheduleReason.FullBatch : urgent ? ScheduleReason.UrgentPartialBatch : ScheduleReason.NoCompatibleIncoming,
                oven.LocationId, version: oven.Version, urgent: urgent, due: load.Where(b => b.Order is not null).Select(b => (DateTime?)b.Order!.DueAt).Min(),
                recipe: recipe.Id, baskets: load.Select(b => b.BasketNumber).ToList());
        }
        if (globalBlock.HasValue)
        {
            blocked.AddRange(actions.Select(a => new ScheduleBlock(a.Reference, PlantEquipment.SharedShuttle, globalBlock.Value)));
            blocked.Add(new(PlantEquipment.SharedShuttle, PlantEquipment.SharedShuttle, globalBlock.Value));
            actions.Clear();
        }
        var ordered = actions.OrderBy(a => a.Rank).ThenByDescending(a => a.Urgent).ThenBy(a => a.DueAt ?? DateTime.MaxValue)
            .ThenBy(a => a.Resource, StringComparer.Ordinal).ThenBy(a => a.Reference, StringComparer.Ordinal).ToList();
        return new(now, ordered, blocked.Distinct().OrderBy(b => b.Reference, StringComparer.Ordinal).ThenBy(b => b.Resource, StringComparer.Ordinal).ToList());
    }
}
