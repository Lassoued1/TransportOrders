using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransportOrders.Core;

public static class OrderRevision
{
    public static string For(TransportOrder order) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { order.OrderNumber, order.Status, order.TransportNumber, order.BasketContentSequence,
            History = order.History.Select(h => new { h.Id, h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy, h.Note }) }))));
}
public sealed record OrderReference(string Number, string Revision)
{
    public static OrderReference From(TransportOrder order) => new(order.OrderNumber, OrderRevision.For(order));
}
public interface IPlantCommandRequest { string ExpectedDatabasePath { get; } string? ExpectedPlantId => null; }
public sealed record QualityCommand(Guid ExpectedVersion, bool VisualPassed, bool QuantityVerified, string Reason,
    string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;
public sealed record BasketReloadCommand(Guid ExpectedVersion, string LotNumber, int Quantity, decimal Weight,
    decimal LoadingLength, string TargetOven, string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;
public sealed record PlcOperatorCommand(Guid ExpectedVersion, string Reason, string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;
public sealed record PlcSettingsCommand(Guid ExpectedVersion, bool Connected, bool SuppressAcknowledgements,
    string Reason, string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;
public sealed record OrderStateCommand(string ExpectedRevision, OrderStatus TargetStatus, string Reason,
    string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;
public sealed record DispatchCommand(IReadOnlyList<OrderReference> Orders, VehicleType Vehicle, PartLoadReason? PartLoadReason,
    string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;
public sealed record TransportArrivalCommand(IReadOnlyList<OrderReference> Orders, string Reason,
    string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;
public sealed record DemoStepCommand(string ExpectedDatabasePath, string? ExpectedPlantId = null) : IPlantCommandRequest;
public sealed record OperatorCommandResult(string Reference);

/// <summary>Serializes the runtime and operator mutations inside the central process.</summary>
public sealed class PlantOperationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await action(); }
        finally { _gate.Release(); }
    }
}

public sealed class PlantCommandService(IOrderRepository repository, TimeProvider clock, PlantOperationGate gate)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private IPlcRepository Plc => repository as IPlcRepository ?? throw new TransportRuleException("PLC persistence is unavailable.");
    private static void Version(Guid version) { if (version == Guid.Empty) throw new ArgumentException("Expected version is required."); }
    private static void Reason(string reason)
    { if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new ArgumentException("A reason of 1–1000 characters is required."); }
    private Task<OperatorCommandResult> Change(string reference, Func<Task> action, CancellationToken ct) =>
        gate.RunAsync(async () => { await action(); return new OperatorCommandResult(reference); }, ct);

    public Task<OperatorCommandResult> InspectAsync(string number, QualityCommand command, string actor, CancellationToken ct = default) =>
        Change(number, async () =>
        {
            Version(command.ExpectedVersion); Reason(command.Reason);
            if (!(await repository.GetPlantStateAsync(ct)).Packages.Any(p => p.Number == number)) throw new KeyNotFoundException();
            await repository.InspectPackageAsync(number, command.ExpectedVersion, command.VisualPassed, command.QuantityVerified,
                actor, command.Reason.Trim(), Now, ct);
        }, ct);
    public Task<OperatorCommandResult> ReloadBasketAsync(string number, BasketReloadCommand command, string actor, CancellationToken ct = default) =>
        Change(number, async () =>
        {
            Version(command.ExpectedVersion);
            if (command.Quantity <= 0 || command.Weight <= 0 || command.Weight > TransportOrder.MaxWeight
                || command.LoadingLength <= 0 || command.LoadingLength > TransportOrder.MaxLoadingLength)
                throw new ArgumentException("Invalid basket quantities or dimensions.");
            var state = await repository.GetPlantStateAsync(ct);
            if (!state.Baskets.Any(b => b.Number == number)) throw new KeyNotFoundException();
            if (!state.MaterialLots.Any(l => l.Number == command.LotNumber)
                || !state.Locations.Any(l => l.Id == command.TargetOven && l.Kind == StationKind.AgingOven))
                throw new ArgumentException("Unknown lot or oven.");
            await repository.ReloadBasketAsync(number, command.ExpectedVersion, command.LotNumber, command.Quantity,
                command.Weight, command.LoadingLength, command.TargetOven, actor, Now, ct);
        }, ct);
    public Task<OperatorCommandResult> SetPlcSettingsAsync(PlcSettingsCommand command, string actor, CancellationToken ct = default) =>
        Change("PLC-SIM", async () =>
        {
            Version(command.ExpectedVersion); Reason(command.Reason);
            await Plc.SetPlcSimulationAsync(command.ExpectedVersion, command.Connected, command.SuppressAcknowledgements,
                actor, command.Reason.Trim(), Now, ct);
        }, ct);
    public Task<OperatorCommandResult> RetryPlcAsync(Guid id, PlcOperatorCommand command, string actor, CancellationToken ct = default) =>
        Change(id.ToString(), async () =>
        {
            Version(command.ExpectedVersion); Reason(command.Reason);
            if (!(await repository.GetPlantStateAsync(ct)).PlcCommands.Any(c => c.Id == id)) throw new KeyNotFoundException();
            await Plc.RetryPlcCommandAsync(id, command.ExpectedVersion, actor, command.Reason.Trim(), Now, ct);
        }, ct);
    public Task<OperatorCommandResult> ConfirmPlcAsync(Guid id, PlcOperatorCommand command, string actor, CancellationToken ct = default) =>
        Change(id.ToString(), async () =>
        {
            Version(command.ExpectedVersion); Reason(command.Reason);
            if (!(await repository.GetPlantStateAsync(ct)).PlcCommands.Any(c => c.Id == id)) throw new KeyNotFoundException();
            if (await Plc.ConfirmPlcArrivalManuallyAsync(id, command.ExpectedVersion, actor, command.Reason.Trim(), Now, ct) == PlcProgress.Deferred)
                throw new TransportRuleException("Equipment must be restored before confirming a physically verified arrival.");
        }, ct);
    public Task<OperatorCommandResult> ChangeOrderAsync(string number, OrderStateCommand command, string actor, CancellationToken ct = default) =>
        Change(number, async () =>
        {
            Reason(command.Reason);
            if (command.TargetStatus is not (OrderStatus.Planned or OrderStatus.OnHold or OrderStatus.Cancelled))
                throw new ArgumentException("Only hold, release and cancellation are operator order transitions.");
            var order = await repository.GetByOrderNumberAsync(number, ct) ?? throw new KeyNotFoundException();
            Fresh(order, command.ExpectedRevision);
            order.TransitionTo(command.TargetStatus, actor, Now, command.Reason.Trim());
            await repository.SaveAsync(order, ct);
        }, ct);
    public Task<OperatorCommandResult> DispatchAsync(DispatchCommand command, string actor, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
        {
            if (!Enum.IsDefined(command.Vehicle) || command.PartLoadReason is { } reason && !Enum.IsDefined(reason))
                throw new ArgumentException("Invalid vehicle or part-load reason.");
            var vehicle = Fleet.Extrusion.SingleOrDefault(v => v.Type == command.Vehicle)
                ?? throw new ArgumentException("Unknown extrusion vehicle.");
            var state = await repository.GetPlantStateAsync(ct);
            var orders = ResolveOrders(state, command.Orders);
            var number = await repository.NextTransportNumberAsync(Now, ct);
            var transport = Transport.Dispatch(number, vehicle, orders, actor, Now, command.PartLoadReason);
            await repository.SaveTransportAsync(transport, orders, ct);
            return new OperatorCommandResult(number);
        }, ct);
    public Task<OperatorCommandResult> ConfirmTransportAsync(string number, TransportArrivalCommand command, string actor, CancellationToken ct = default) =>
        Change(number, async () =>
        {
            Reason(command.Reason);
            var state = await repository.GetPlantStateAsync(ct);
            var transport = state.Transports.SingleOrDefault(t => t.TransportNumber == number) ?? throw new KeyNotFoundException();
            if (transport.Status != TransportStatus.InTransit) throw new TransportRuleException("Transport already arrived.");
            var orders = ResolveOrders(state, command.Orders);
            var members = state.Orders.Where(o => o.TransportNumber == number).Select(o => o.OrderNumber).Order().ToArray();
            if (!members.SequenceEqual(orders.Select(o => o.OrderNumber).Order()))
                throw new TransportRuleException("Arrival must cover the complete transport membership.");
            var plc = state.PlcCommands.SingleOrDefault(c => c.Kind == PlcCommandKind.MaterialTransfer && c.Operation == number)
                ?? throw new TransportRuleException("No PLC command exists for this transport.");
            if (await Plc.ConfirmPlcArrivalManuallyAsync(plc.Id, plc.Version, actor, command.Reason.Trim(), Now, ct) == PlcProgress.Deferred)
                throw new TransportRuleException("Equipment must be restored before confirming arrival.");
        }, ct);
    public Task<SimulationResult> StepAsync(CancellationToken ct = default) =>
        gate.RunAsync(() => new ExtrusionSimulator(repository, clock).StepAsync(ct), ct);
    private static void Fresh(TransportOrder order, string revision)
    {
        if (string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("Order revision is required.");
        if (OrderRevision.For(order) != revision) throw new TransportRuleException("Order changed since the station loaded it.");
    }
    private static List<TransportOrder> ResolveOrders(PlantState state, IReadOnlyList<OrderReference> references)
    {
        if (references is null || references.Count is < 1 or > 100 || references.Any(r => r is null)
            || references.Select(r => r.Number).Distinct().Count() != references.Count)
            throw new ArgumentException("A distinct, nonempty order selection is required.");
        return references.Select(r =>
        {
            var order = state.Orders.SingleOrDefault(o => o.OrderNumber == r.Number) ?? throw new KeyNotFoundException();
            Fresh(order, r.Revision); return order;
        }).ToList();
    }
}
