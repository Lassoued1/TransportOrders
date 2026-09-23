namespace TransportOrders.Core;

public interface IOrderRepository
{
    Task<IReadOnlyList<TransportOrder>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TransportOrder order, CancellationToken cancellationToken = default);
}
