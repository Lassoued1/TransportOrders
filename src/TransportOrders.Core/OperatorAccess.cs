namespace TransportOrders.Core;

public static class PlantPermissions
{
    public const string Equipment = "equipment.control";
    public const string Quality = "quality.inspect";
    public const string Basket = "basket.reload";
    public const string OrderQuality = "order.hold-release";
    public const string OrderCancel = "order.cancel";
    public const string Dispatch = "transport.dispatch";
    public const string Arrival = "transport.manual-arrival";
    public const string Plc = "plc.operator";
    public const string Simulation = "simulation.step";
    public static IReadOnlyList<string> All { get; } = [Equipment, Quality, Basket, OrderQuality, OrderCancel, Dispatch, Arrival, Plc, Simulation];
}
public sealed record OperatorAccessSnapshot(string User, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, string DatabasePath);
