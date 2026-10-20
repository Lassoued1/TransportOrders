using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class SqlitePlcSimulator(IDbContextFactory<AppDbContext> factory) : IPlcChannel
{
    public async Task<PlcAcknowledgement?> SendAsync(PlcCommand command, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var settings = await context.PlcSimulation.SingleAsync(cancellationToken);
        if (!settings.Connected) throw new PlcConnectionException("Demo PLC connection is offline.");
        var stored = await context.PlcCommands.SingleAsync(c => c.Id == command.Id, cancellationToken);
        if (stored.ContractVersion != 1 || stored.PayloadHash != command.PayloadHash || stored.Payload != command.Payload
            || stored.EquipmentId != command.EquipmentId || stored.Target != command.Target || stored.Source != command.Source)
            throw new TransportRuleException("PLC command envelope changed or its contract version is unsupported.");
        var execution = await context.PlcExecutions.SingleOrDefaultAsync(e => e.CommandId == command.Id, cancellationToken);
        if (execution is null) { execution = new(command); context.PlcExecutions.Add(execution); }
        else if (execution.PayloadHash != command.PayloadHash) throw new TransportRuleException("Command id reuse with another payload is forbidden.");
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return settings.SuppressAcknowledgements ? null : execution.Reply();
    }
    public async Task<PlcAcknowledgement?> ReceiveAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var settings = await context.PlcSimulation.SingleAsync(cancellationToken);
        if (!settings.Connected) throw new PlcConnectionException("Demo PLC connection is offline.");
        if (settings.SuppressAcknowledgements) return null;
        return (await context.PlcExecutions.SingleOrDefaultAsync(e => e.CommandId == commandId, cancellationToken))?.Reply();
    }
}
