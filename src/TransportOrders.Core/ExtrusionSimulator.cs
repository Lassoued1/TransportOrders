using System.Globalization;

namespace TransportOrders.Core;

public enum SimulationAction { Waiting, Dispatched, Arrived, ProcessCompleted, Faulted, CycleStarted, CycleAdvanced, CycleCompleted,
    PackingStarted, PackingCompleted, PackageChecked, PackageDispatched, PackageShipped, ReturnStarted, ReturnArrived,
    PlcWaiting, PlcAccepted, PlcTimedOut, PlcRejected, PlcOffline, PlcDeferred, CentralService }
public sealed record SimulationResult(SimulationAction Action, string? Reference = null);

/// <summary>Executes one action from the same deterministic schedule shown by the UI and API.</summary>
public sealed partial class ExtrusionSimulator(IOrderRepository repository, TimeProvider clock, IPlcChannel? plcChannel = null,
    IPlantExecutionGate? executionGate = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static Vehicle Shuttle { get; } = new(VehicleType.BasketShuttle, 7m, 4m);
    public Task<PlantState> GetPlantStateAsync(CancellationToken cancellationToken = default) => repository.GetPlantStateAsync(cancellationToken);
    public ProductionSchedule GetSchedule(PlantState state) => ProductionScheduler.Plan(state, clock.GetUtcNow().UtcDateTime);
    public async Task SetEquipmentControlAsync(string equipmentId, Guid expectedVersion, EquipmentMode mode,
        EquipmentCondition condition, string user, string reason, CancellationToken cancellationToken = default) =>
        await repository.SetEquipmentControlAsync(equipmentId, expectedVersion, mode, condition, user, reason,
            clock.GetUtcNow().UtcDateTime, cancellationToken);

    public async Task<SimulationResult> StepAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var ownership = executionGate?.TryEnter();
            if (executionGate is not null && ownership is null)
                return new(SimulationAction.CentralService);
            var state = await repository.GetPlantStateAsync(cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            var action = ProductionScheduler.Plan(state, now).Next;
            if (action is null)
                return new(state.Equipment.Any(e => e.Id == PlantEquipment.SharedShuttle && !e.CanOperate)
                    ? SimulationAction.Faulted : SimulationAction.Waiting);
            if (action.Kind is ScheduledActionKind.ArriveTransport or ScheduledActionKind.ArriveReturn or ScheduledActionKind.ArrivePackage
                && repository is IPlcRepository plc)
            {
                var kind = action.Kind == ScheduledActionKind.ArriveTransport ? PlcCommandKind.MaterialTransfer
                    : action.Kind == ScheduledActionKind.ArriveReturn ? PlcCommandKind.EmptyReturn : PlcCommandKind.PackageTransfer;
                var command = state.PlcCommands.Single(c => c.Kind == kind && c.Operation == action.Reference);
                var progress = await new PlcExchangeService(repository, plcChannel ?? plc.PlcChannel, clock).ProcessAsync(command.Id, cancellationToken);
                if (progress == PlcProgress.Applied)
                    return new(action.Kind == ScheduledActionKind.ArriveTransport ? SimulationAction.Arrived
                        : action.Kind == ScheduledActionKind.ArriveReturn ? SimulationAction.ReturnArrived : SimulationAction.PackageShipped,
                        action.Kind == ScheduledActionKind.ArriveReturn ? action.BasketNumbers.Single() : action.Reference);
                return new(progress switch { PlcProgress.Accepted => SimulationAction.PlcAccepted, PlcProgress.TimedOut => SimulationAction.PlcTimedOut,
                    PlcProgress.Rejected => SimulationAction.PlcRejected, PlcProgress.Offline => SimulationAction.PlcOffline,
                    PlcProgress.Deferred => SimulationAction.PlcDeferred, _ => SimulationAction.PlcWaiting }, command.Operation);
            }
            switch (action.Kind)
            {
                case ScheduledActionKind.ArriveTransport:
                    var moving = state.Transports.Single(t => t.TransportNumber == action.Reference);
                    var load = state.Orders.Where(o => o.TransportNumber == moving.TransportNumber).ToList();
                    moving.Complete(load, "PLC-SIM", now);
                    await repository.SaveTransportAsync(moving, load, cancellationToken);
                    return new(SimulationAction.Arrived, action.Reference);
                case ScheduledActionKind.ArriveReturn:
                    await repository.CompleteEmptyReturnAsync(Guid.Parse(action.Reference), action.ExpectedVersion, "PLC-SIM", now, cancellationToken);
                    return new(SimulationAction.ReturnArrived, action.BasketNumbers.Single());
                case ScheduledActionKind.ArrivePackage:
                    await repository.CompletePackageShippingAsync(action.Reference, action.ExpectedVersion, "PLC-SIM", now, cancellationToken);
                    return new(SimulationAction.PackageShipped, action.Reference);
                case ScheduledActionKind.CompletePacking:
                    await repository.CompletePackingAsync(action.Reference, action.ExpectedVersion, "PACK-SIM", now, cancellationToken);
                    return new(SimulationAction.PackingCompleted, action.Reference);
                case ScheduledActionKind.InspectPackage:
                    await repository.InspectPackageAsync(action.Reference, action.ExpectedVersion, true, true, "QA-SIM", "Simulated visual and quantity checks", now, cancellationToken);
                    return new(SimulationAction.PackageChecked, action.Reference);
                case ScheduledActionKind.StartPacking:
                    await repository.StartPackingAsync(action.Reference, action.ExpectedVersion, "PACK-SIM", now, cancellationToken);
                    return new(SimulationAction.PackingStarted, action.Reference);
                case ScheduledActionKind.ReturnEmpty:
                    await repository.StartEmptyReturnAsync(action.Reference, action.ExpectedVersion, action.Target!, "RETURN-SIM", now, cancellationToken);
                    return new(SimulationAction.ReturnStarted, action.Reference);
                case ScheduledActionKind.ShipPackage:
                    await repository.StartPackageShippingAsync(action.Reference, action.ExpectedVersion, "SHIP-SIM", now, cancellationToken);
                    return new(SimulationAction.PackageDispatched, action.Reference);
                case ScheduledActionKind.StartOvenCycle:
                    await repository.StartOvenCycleAsync(action.Resource, action.RecipeId!, action.BasketNumbers, "PROCESS-SIM", now, cancellationToken);
                    return new(SimulationAction.CycleStarted, action.Resource);
                case ScheduledActionKind.AdvanceOvenCycle:
                    var cycle = state.Cycles.Single(c => c.Id.ToString() == action.Reference);
                    await repository.AdvanceOvenCycleAsync(cycle.Id, action.ExpectedVersion, 1, "PROCESS-SIM", now, cancellationToken);
                    return new(cycle.ElapsedMinutes + 1 >= cycle.TotalMinutes ? SimulationAction.CycleCompleted : SimulationAction.CycleAdvanced, action.Resource);
                case ScheduledActionKind.CreatePackingOrder:
                    var basket = state.Locate().Single(b => b.BasketNumber == action.Reference);
                    var prefix = $"TO-{now.Year}-";
                    var sequence = state.Orders.Where(o => o.OrderNumber.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(o => int.Parse(o.OrderNumber[prefix.Length..], CultureInfo.InvariantCulture)).DefaultIfEmpty().Max() + 1;
                    if (sequence > 9999) throw new TransportRuleException("The demo order number range is exhausted.");
                    var following = TransportOrder.ForBasket($"{prefix}{sequence:D4}", basket.Basket, basket.MaterialLot,
                        action.Target!, basket.Order!.Priority, now.AddHours(1), now, "SCHEDULER-SIM");
                    await repository.SaveAsync(following, cancellationToken);
                    return new(SimulationAction.ProcessCompleted, basket.BasketNumber);
                case ScheduledActionKind.DispatchBasket:
                    var candidate = state.Locate().Single(b => b.BasketNumber == action.Reference);
                    var number = await repository.NextTransportNumberAsync(now, cancellationToken);
                    var transport = Transport.Dispatch(number, Shuttle, [candidate.Order!], "SCHEDULER-SIM", now,
                        candidate.Order!.Priority == OrderPriority.Urgent ? PartLoadReason.UrgentMaterial : PartLoadReason.NoMatchingMaterial);
                    await repository.SaveTransportAsync(transport, [candidate.Order], cancellationToken);
                    return new(SimulationAction.Dispatched, number);
                default: throw new ArgumentOutOfRangeException(nameof(action.Kind));
            }
        }
        finally { _gate.Release(); }
    }
    public Task SetPlcSimulationAsync(Guid version, bool connected, bool suppress, string user, string reason) =>
        (repository as IPlcRepository ?? throw new InvalidOperationException("PLC persistence is unavailable."))
            .SetPlcSimulationAsync(version, connected, suppress, user, reason, clock.GetUtcNow().UtcDateTime);
    public Task RetryPlcCommandAsync(Guid id, Guid version, string user, string reason) =>
        (repository as IPlcRepository ?? throw new InvalidOperationException("PLC persistence is unavailable."))
            .RetryPlcCommandAsync(id, version, user, reason, clock.GetUtcNow().UtcDateTime);
    public async Task ConfirmPlcArrivalManuallyAsync(Guid id, Guid version, string user, string reason)
    {
        var result = await (repository as IPlcRepository ?? throw new InvalidOperationException("PLC persistence is unavailable."))
            .ConfirmPlcArrivalManuallyAsync(id, version, user, reason, clock.GetUtcNow().UtcDateTime);
        if (result == PlcProgress.Deferred) throw new TransportRuleException("Restore unavailable equipment before confirming the arrival.");
    }
}
