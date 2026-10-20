using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class EfAlarmRepository(IDbContextFactory<AppDbContext> factory) : IAlarmRepository
{
    public async Task SynchronizeAsync(IReadOnlyList<AlarmCondition> conditions, DateTime now, CancellationToken ct = default)
    {
        var desired = conditions.ToDictionary(c => c.Key, StringComparer.Ordinal);
        await using var context = await factory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var active = await context.Alarms.Include(a => a.History).Where(a => a.IsActive).ToListAsync(ct);
        foreach (var alarm in active)
            if (desired.Remove(alarm.Key, out var condition)) alarm.UpdateDetail(condition.Detail, now);
            else alarm.Clear(now);
        context.Alarms.AddRange(desired.Values.Select(c => new PlantAlarm(c, now)));
        await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<PlantAlarmSnapshot>> ReadAsync(CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        var alarms = await context.Alarms.AsNoTracking().Include(a => a.History).OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.RaisedUtc).ToListAsync(ct);
        return alarms.Select(PlantAlarmSnapshot.From).ToArray();
    }
    public async Task AcknowledgeAsync(Guid id, Guid version, string actor, string reason, DateTime now, CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        var alarm = await context.Alarms.Include(a => a.History).SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new KeyNotFoundException();
        alarm.Acknowledge(version, actor, reason, now); await context.SaveChangesAsync(ct);
    }
}
