namespace TransportOrders.Core;

public sealed partial class ExtrusionSimulator
{
    public Task InspectPackageAsync(string number, Guid version, bool visual, bool quantity, string user, string reason) =>
        repository.InspectPackageAsync(number, version, visual, quantity, user, reason, clock.GetUtcNow().UtcDateTime);
    public Task ReloadBasketAsync(string number, Guid version, string lot, int quantity, decimal weight, decimal length, string oven, string user) =>
        repository.ReloadBasketAsync(number, version, lot, quantity, weight, length, oven, user, clock.GetUtcNow().UtcDateTime);
}
