using TransportOrders.Core;
namespace TransportOrders.App.Services;

public sealed class RemotePlantRepository(PlantConnection connection) : IOrderRepository
{
    public Task<PlantState> GetPlantStateAsync(CancellationToken cancellationToken = default) => connection.ReadAsync(cancellationToken);
    public async Task<IReadOnlyList<TransportOrder>> GetAllAsync(CancellationToken cancellationToken = default) => (await GetPlantStateAsync(cancellationToken)).Orders;
    public async Task<TransportOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => (await GetAllAsync(cancellationToken)).FirstOrDefault(o => o.Id == id);
    public async Task<TransportOrder?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default) => (await GetAllAsync(cancellationToken)).FirstOrDefault(o => string.Equals(o.OrderNumber, orderNumber, StringComparison.OrdinalIgnoreCase));
    public async Task<IReadOnlyList<Transport>> GetTransportsAsync(CancellationToken cancellationToken = default) => (await GetPlantStateAsync(cancellationToken)).Transports;
    public Task StartPackingAsync(string basketNumber, Guid basketVersion, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task CompletePackingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task InspectPackageAsync(string packageNumber, Guid version, bool visualPassed, bool quantityVerified,
        string user, string reason, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task StartPackageShippingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task CompletePackageShippingAsync(string packageNumber, Guid version, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task StartEmptyReturnAsync(string basketNumber, Guid version, string target, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task CompleteEmptyReturnAsync(Guid returnId, Guid version, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task ReloadBasketAsync(string basketNumber, Guid version, string lotNumber, int quantity, decimal weight,
        decimal loadingLength, string targetOven, string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task SetEquipmentControlAsync(string equipmentId, Guid expectedVersion, EquipmentMode mode,
        EquipmentCondition condition, string user, string reason, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task StartOvenCycleAsync(string equipmentId, string recipeId, IReadOnlyCollection<string> basketNumbers,
        string user, DateTime now, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task AdvanceOvenCycleAsync(Guid cycleId, Guid expectedVersion, int minutes, string user, DateTime now,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task SaveAsync(TransportOrder order, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task<string> NextTransportNumberAsync(DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
    public Task SaveTransportAsync(Transport transport, IReadOnlyCollection<TransportOrder> orders, CancellationToken cancellationToken = default) => throw new NotSupportedException("Desktop writes must use authenticated API commands.");
}
