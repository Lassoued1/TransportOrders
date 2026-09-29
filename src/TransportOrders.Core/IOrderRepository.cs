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

    /// <summary>All transports, newest first.</summary>
    Task<IReadOnlyList<Transport>> GetTransportsAsync(CancellationToken cancellationToken = default);

    /// <summary>The next free number, e.g. TR-2026-0007.</summary>
    Task<string> NextTransportNumberAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>Saves a transport together with its orders in one transaction.</summary>
    Task SaveTransportAsync(Transport transport, IReadOnlyCollection<TransportOrder> orders, CancellationToken cancellationToken = default);
}
