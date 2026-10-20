namespace TransportOrders.Core;

public interface IOrderRepository
{
    Task StartPackingAsync(string basketNumber, Guid basketVersion, string user, DateTime now, CancellationToken cancellationToken = default);
    Task CompletePackingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default);
    Task InspectPackageAsync(string packageNumber, Guid version, bool visualPassed, bool quantityVerified,
        string user, string reason, DateTime now, CancellationToken cancellationToken = default);
    Task StartPackageShippingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default);
    Task CompletePackageShippingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default);
    Task StartEmptyReturnAsync(string basketNumber, Guid version, string target, string user, DateTime now, CancellationToken cancellationToken = default);
    Task CompleteEmptyReturnAsync(Guid returnId, Guid version, string user, DateTime now, CancellationToken cancellationToken = default);
    Task ReloadBasketAsync(string basketNumber, Guid version, string lotNumber, int quantity, decimal weight,
        decimal loadingLength, string targetOven, string user, DateTime now, CancellationToken cancellationToken = default);
    Task SetEquipmentControlAsync(string equipmentId, Guid expectedVersion, EquipmentMode mode,
        EquipmentCondition condition, string user, string reason, DateTime now, CancellationToken cancellationToken = default);
    Task StartOvenCycleAsync(string equipmentId, string recipeId, IReadOnlyCollection<string> basketNumbers,
        string user, DateTime now, CancellationToken cancellationToken = default);
    Task AdvanceOvenCycleAsync(Guid cycleId, Guid expectedVersion, int minutes, string user, DateTime now,
        CancellationToken cancellationToken = default);
    /// <summary>Consistent snapshot of physical baskets, lots, locations and transport history.</summary>
    Task<PlantState> GetPlantStateAsync(CancellationToken cancellationToken = default);

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
