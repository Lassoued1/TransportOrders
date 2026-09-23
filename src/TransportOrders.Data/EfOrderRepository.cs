using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class EfOrderRepository(IDbContextFactory<AppDbContext> contextFactory) : IOrderRepository
{
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

        // Update attaches the whole aggregate: existing history rows are kept, new ones (Id = 0) are inserted.
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Orders.Update(order);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<TransportOrder> WithHistory(AppDbContext context) =>
        context.Orders
            .AsNoTracking()
            .Include(o => o.History.OrderBy(h => h.ChangedAt).ThenBy(h => h.Id));
}
