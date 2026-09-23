namespace TransportOrders.Core;

public interface IOrderRepository
{
    /// <summary>All orders with their status history, ordered by order number.</summary>
    Task<IReadOnlyList<TransportOrder>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<TransportOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive lookup, e.g. for a scanned barcode.</summary>
    Task<TransportOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>Saves the order's current state and any new status history entries.</summary>
    Task SaveAsync(TransportOrder order, CancellationToken cancellationToken = default);
}
