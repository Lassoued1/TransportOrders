namespace TransportOrders.Core;

public sealed record PlantStateSnapshot(
    IReadOnlyList<BasketSnapshot> Baskets, IReadOnlyList<MaterialLotSnapshot> MaterialLots,
    IReadOnlyList<PlantLocationSnapshot> Locations, IReadOnlyList<TransportOrderSnapshot> Orders,
    IReadOnlyList<TransportSnapshot> Transports, IReadOnlyList<PlantEquipmentSnapshot> Equipment,
    IReadOnlyList<OvenRecipeSnapshot> Recipes, IReadOnlyList<OvenCycleSnapshot> Cycles,
    IReadOnlyList<PackedPackageSnapshot> Packages, IReadOnlyList<BasketReturnSnapshot> Returns,
    IReadOnlyList<PlcCommandSnapshot> PlcCommands, PlcSimulationSettingsSnapshot? PlcSimulation)
{
    public static PlantStateSnapshot From(PlantState state) => new(
        state.Baskets.Select(BasketSnapshot.From).ToArray(), state.MaterialLots.Select(MaterialLotSnapshot.From).ToArray(),
        state.Locations.Select(PlantLocationSnapshot.From).ToArray(), state.Orders.Select(TransportOrderSnapshot.From).ToArray(),
        state.Transports.Select(TransportSnapshot.From).ToArray(), state.Equipment.Select(PlantEquipmentSnapshot.From).ToArray(),
        state.Recipes.Select(OvenRecipeSnapshot.From).ToArray(), state.Cycles.Select(OvenCycleSnapshot.From).ToArray(),
        state.Packages.Select(PackedPackageSnapshot.From).ToArray(), state.Returns.Select(BasketReturnSnapshot.From).ToArray(),
        state.PlcCommands.Select(PlcCommandSnapshot.From).ToArray(), state.PlcSimulation is null ? null : PlcSimulationSettingsSnapshot.From(state.PlcSimulation));
    public PlantState Restore() => new(Baskets.Select(Basket.FromSnapshot).ToArray(), MaterialLots.Select(MaterialLot.FromSnapshot).ToArray(),
        Locations.Select(PlantLocation.FromSnapshot).ToArray(), Orders.Select(TransportOrder.FromSnapshot).ToArray(),
        Transports.Select(Transport.FromSnapshot).ToArray())
    {
        Equipment = Equipment.Select(PlantEquipment.FromSnapshot).ToArray(), Recipes = Recipes.Select(OvenRecipe.FromSnapshot).ToArray(),
        Cycles = Cycles.Select(OvenCycle.FromSnapshot).ToArray(), Packages = Packages.Select(PackedPackage.FromSnapshot).ToArray(),
        Returns = Returns.Select(BasketReturn.FromSnapshot).ToArray(), PlcCommands = PlcCommands.Select(PlcCommand.FromSnapshot).ToArray(),
        PlcSimulation = PlcSimulation is null ? null : PlcSimulationSettings.FromSnapshot(PlcSimulation)
    };
}
public sealed record ClientRuntimeState(bool Enabled, bool Running, DateTime? LastTickUtc, string? LastError);
public sealed record PlantClientSnapshot(int ContractVersion, string PlantId, string Name, string TimeZoneId,
    DateTime CapturedAtUtc, PlantStateSnapshot State, OperatorAccessSnapshot Access, ClientRuntimeState Runtime,
    IReadOnlyList<PlantAlarmSnapshot>? Alarms = null, string OperatingMode = "Demo");
