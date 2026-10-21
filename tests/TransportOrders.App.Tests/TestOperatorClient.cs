using TransportOrders.App.Services;
using TransportOrders.Core;

namespace TransportOrders.App.Tests;

// A test-only stand-in for the remote service. Production view models never receive a repository writer.
internal sealed class TestOperatorClient(IOrderRepository repository, TimeProvider clock, string actor) : IOperatorCommandClient
{
    private readonly PlantCommandService _service = new(repository, clock, new());
    public async Task InspectAsync(string number, Guid version, bool visual, bool quantity, string reason) =>
        _ = await _service.InspectAsync(number, new(version, visual, quantity, reason, "test"), actor);
    public async Task ReloadBasketAsync(string number, Guid version, string lot, int quantity, decimal weight, decimal length, string oven) =>
        _ = await _service.ReloadBasketAsync(number, new(version, lot, quantity, weight, length, oven, "test"), actor);
    public async Task SetPlcSettingsAsync(Guid version, bool connected, bool suppress, string reason) =>
        _ = await _service.SetPlcSettingsAsync(new(version, connected, suppress, reason, "test"), actor);
    public async Task RetryPlcAsync(Guid id, Guid version, string reason) =>
        _ = await _service.RetryPlcAsync(id, new(version, reason, "test"), actor);
    public async Task ConfirmPlcAsync(Guid id, Guid version, string reason) =>
        _ = await _service.ConfirmPlcAsync(id, new(version, reason, "test"), actor);
    public async Task ChangeOrderAsync(TransportOrder order, OrderStatus target, string reason)
    {
        // The generic UI fake predates the physical extrusion model.
        var current = await repository.GetByOrderNumberAsync(order.OrderNumber) ?? throw new KeyNotFoundException();
        if (OrderRevision.For(current) != OrderRevision.For(order)) throw new TransportRuleException("Stale order.");
        current.TransitionTo(target, actor, clock.GetUtcNow().UtcDateTime, reason);
        await repository.SaveAsync(current);
    }
    public async Task<string> DispatchAsync(LoadProposal proposal, PartLoadReason? reason)
    {
        var orders = new List<TransportOrder>();
        foreach (var order in proposal.Orders)
            orders.Add(await repository.GetByOrderNumberAsync(order.OrderNumber) ?? throw new KeyNotFoundException());
        var number = await repository.NextTransportNumberAsync(clock.GetUtcNow().UtcDateTime);
        var transport = Transport.Dispatch(number, proposal.Vehicle, orders, actor, clock.GetUtcNow().UtcDateTime, reason);
        await repository.SaveTransportAsync(transport, orders);
        return number;
    }
    public async Task ConfirmTransportAsync(string number, IReadOnlyList<TransportOrder> orders, string reason)
    {
        if (repository is IPlcRepository)
        { await _service.ConfirmTransportAsync(number, new(orders.Select(OrderReference.From).ToArray(), reason, "test"), actor); return; }
        var current = (await repository.GetTransportsAsync()).Single(t => t.TransportNumber == number);
        var members = (await repository.GetAllAsync()).Where(o => o.TransportNumber == number).ToList();
        current.Complete(members, actor, clock.GetUtcNow().UtcDateTime);
        await repository.SaveTransportAsync(current, members);
    }
    public Task<SimulationResult> StepAsync() => _service.StepAsync();
    public Task SetRuntimeAsync(Guid version, bool paused, string reason) => throw new NotSupportedException();
}
