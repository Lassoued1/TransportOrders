using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

public sealed class EfOrderRepository(IDbContextFactory<AppDbContext> contextFactory) : IOrderRepository
{
    public async Task<IReadOnlyList<TransportOrder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Orders
            .AsNoTracking()
            .OrderBy(o => o.OrderNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(TransportOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Orders.Update(order);
        await context.SaveChangesAsync(cancellationToken);
    }
}
