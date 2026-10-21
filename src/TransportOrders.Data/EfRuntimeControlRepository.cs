using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class EfRuntimeControlRepository(IDbContextFactory<AppDbContext> factory) : IRuntimeControlRepository
{
    public async Task<RuntimeControlState> ReadAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.RuntimeControl.AsNoTracking().SingleAsync(ct);
    }

    public async Task<RuntimeControlState> ChangeAsync(RuntimeControlCommand command, string actor, DateTime now, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000)
            throw new ArgumentException("A reason of 1–1000 characters is required.");
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("An authenticated actor is required.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var state = await db.RuntimeControl.SingleAsync(ct);
        if (command.ExpectedVersion == Guid.Empty || state.Version != command.ExpectedVersion)
            throw new TransportRuleException("Runtime controls changed. Refresh and retry.");
        if (state.Paused == command.Paused) return state;
        state.Paused = command.Paused;
        state.Version = Guid.NewGuid();
        db.RuntimeControlEvents.Add(new() { Paused = state.Paused, Version = state.Version,
            Actor = actor, Reason = command.Reason.Trim(), AtUtc = now });
        // State and audit event commit atomically; Version guards concurrent service instances.
        await db.SaveChangesAsync(ct);
        return state;
    }

    public async Task<IReadOnlyList<RuntimeControlEvent>> HistoryAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.RuntimeControlEvents.AsNoTracking().OrderByDescending(e => e.AtUtc).Take(100).ToArrayAsync(ct);
    }
}
