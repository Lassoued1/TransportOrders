using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed partial class EfOrderRepository
{
    public IPlcChannel PlcChannel => new SqlitePlcSimulator(contextFactory);
    public async Task<PlcCommand> BeginPlcAttemptAsync(Guid commandId, Guid version, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var command = await context.PlcCommands.SingleAsync(c => c.Id == commandId, cancellationToken);
        CheckVersion(command.Version, version); command.BeginAttempt(now);
        await context.SaveChangesAsync(cancellationToken); return command;
    }
    public async Task<PlcReceiptResult> RecordPlcAckAsync(PlcAcknowledgement ack, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var command = await context.PlcCommands.SingleOrDefaultAsync(c => c.Id == ack.CommandId, cancellationToken)
            ?? throw new TransportRuleException("Unknown PLC command correlation id.");
        command.Validate(ack);
        var previous = await context.PlcInbox.SingleOrDefaultAsync(e => e.Id == ack.EventId, cancellationToken);
        if (previous is not null)
        {
            if (previous.Payload != JsonSerializer.Serialize(ack)) throw new TransportRuleException("Acknowledgement id reused with different content.");
            return PlcReceiptResult.Duplicate;
        }
        var result = command.Receive(ack, now) ? PlcReceiptResult.Recorded : PlcReceiptResult.Stale;
        context.PlcInbox.Add(new(ack, result, now));
        await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return result;
    }
    public async Task ExpirePlcCommandAsync(Guid commandId, Guid version, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var command = await context.PlcCommands.SingleAsync(c => c.Id == commandId, cancellationToken);
        CheckVersion(command.Version, version); command.Expire(now); await context.SaveChangesAsync(cancellationToken);
    }
    public async Task RetryPlcCommandAsync(Guid commandId, Guid version, string user, string reason, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var command = await context.PlcCommands.SingleAsync(c => c.Id == commandId, cancellationToken);
        CheckVersion(command.Version, version); command.Retry(user, reason, now); await context.SaveChangesAsync(cancellationToken);
    }
    public async Task SetPlcSimulationAsync(Guid version, bool connected, bool suppressAcknowledgements, string user, string reason, DateTime now, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await context.PlcSimulation.SingleAsync(cancellationToken);
        CheckVersion(settings.Version, version); settings.Set(connected, suppressAcknowledgements, user, reason, now);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<PlcProgress> ApplyPlcCompletionAsync(Guid commandId, DateTime now, CancellationToken cancellationToken = default) =>
        ApplyArrivalAsync(commandId, null, "PLC-ACK", null, now, cancellationToken);
    public Task<PlcProgress> ConfirmPlcArrivalManuallyAsync(Guid commandId, Guid version, string user, string reason, DateTime now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user); ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return ApplyArrivalAsync(commandId, version, user, reason, now, cancellationToken);
    }
    private async Task<PlcProgress> ApplyArrivalAsync(Guid commandId, Guid? expectedVersion, string actor, string? manualReason, DateTime now, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var command = await context.PlcCommands.SingleAsync(c => c.Id == commandId, cancellationToken);
        if (expectedVersion.HasValue) CheckVersion(command.Version, expectedVersion.Value);
        if (command.Status == PlcCommandStatus.Applied) return PlcProgress.Applied;
        if (manualReason is null && command.Status != PlcCommandStatus.Completed) throw new TransportRuleException("No completed PLC acknowledgement is available.");
        var equipment = await context.Equipment.Where(e => e.Id == command.EquipmentId || e.LocationId == command.Target).ToListAsync(cancellationToken);
        var shuttle = equipment.Single(e => e.Id == command.EquipmentId);
        if (equipment.Any(e => !e.CanOperate || (manualReason is null && !e.CanSimulate) || (e.Id != command.EquipmentId && e.ActiveOperation is not null))) return PlcProgress.Deferred;
        switch (command.Kind)
        {
            case PlcCommandKind.MaterialTransfer:
                var transport = await context.Transports.SingleAsync(t => t.TransportNumber == command.Operation, cancellationToken);
                if (transport.SourceLocation != command.Source || transport.TargetLocation != command.Target) throw new TransportRuleException("Transfer route no longer matches PLC command.");
                var orders = await context.Orders.Include(o => o.History).Where(o => o.TransportNumber == transport.TransportNumber).ToListAsync(cancellationToken);
                var numbers = orders.Select(o => o.PieceNumber).ToList();
                var baskets = await context.Baskets.Where(b => numbers.Contains(b.Number)).ToListAsync(cancellationToken);
                EnsurePayload(command, baskets.Select(b => new PlcLoadItem(b.Number, b.ContentSequence, b.Quantity, b.Weight, b.LoadingLength)));
                foreach (var order in orders) await ValidateContentsAsync(context, baskets.Single(b => b.Number == order.PieceNumber), order, cancellationToken);
                transport.Complete(orders, actor, now);
                foreach (var basket in baskets) basket.Arrive(transport.TransportNumber);
                shuttle.Finish(transport.TransportNumber, actor, now);
                break;
            case PlcCommandKind.PackageTransfer:
                var package = await context.Packages.SingleAsync(p => p.Number == command.Operation, cancellationToken);
                if (package.ReservedLocationId != command.Target) throw new TransportRuleException("Package destination no longer matches command.");
                EnsurePayload(command, [new(package.Number, package.ContentSequence, package.Quantity, package.Weight, package.LoadingLength)]);
                package.Arrive(actor, now); shuttle.Finish(package.Number, actor, now);
                break;
            case PlcCommandKind.EmptyReturn:
                var id = Guid.Parse(command.Operation);
                var movement = await context.BasketReturns.SingleAsync(r => r.Id == id, cancellationToken);
                var empty = await context.Baskets.SingleAsync(b => b.Number == movement.BasketNumber, cancellationToken);
                if (movement.SourceLocation != command.Source || movement.TargetLocation != command.Target) throw new TransportRuleException("Return route no longer matches command.");
                EnsurePayload(command, [new(empty.Number, empty.ContentSequence, 0, empty.Weight, empty.LoadingLength)]);
                movement.Complete(actor, now); empty.ArriveEmpty(movement, actor, now);
                shuttle.Finish($"RETURN:{movement.Id}", actor, now);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(command.Kind));
        }
        if (manualReason is null) command.MarkApplied(now, actor);
        else command.ConfirmManually(actor, now, manualReason);
        await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return PlcProgress.Applied;
    }
    private static void EnsurePayload(PlcCommand command, IEnumerable<PlcLoadItem> items)
    {
        var payload = PlcCommand.SerializePayload(items);
        if (payload != command.Payload) throw new TransportRuleException("The physical load no longer matches the PLC envelope.");
    }
    private static async Task MarkManualPlcArrivalAsync(AppDbContext context, PlcCommandKind kind, string operation, string user, DateTime now, CancellationToken cancellationToken)
    {
        var command = await context.PlcCommands.SingleOrDefaultAsync(c => c.Kind == kind && c.Operation == operation, cancellationToken);
        if (command is not null) command.ConfirmManually(user, now);
    }
}
