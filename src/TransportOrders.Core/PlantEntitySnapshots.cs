namespace TransportOrders.Core;

// Explicit wire data: constructors are DTO constructors, not domain transitions.

public sealed record BasketSnapshot(
    string Number,
    string MaterialLotNumber,
    int ContentSequence,
    int Quantity,
    decimal Weight,
    decimal LoadingLength,
    string? LocationId,
    string? ReservedLocationId,
    string? ActiveTransportNumber,
    Guid? ActiveReturnId,
    Guid Version,
    IReadOnlyList<BasketEventSnapshot> History)
{
    public static BasketSnapshot From(Basket value) => new(value.Number, value.MaterialLotNumber, value.ContentSequence, value.Quantity, value.Weight, value.LoadingLength, value.LocationId, value.ReservedLocationId, value.ActiveTransportNumber, value.ActiveReturnId, value.Version, value.History.Select(BasketEventSnapshot.From).ToArray());
}

public sealed record BasketEventSnapshot(
    int Id,
    string Action,
    string Reference,
    string User,
    DateTime At)
{
    public static BasketEventSnapshot From(BasketEvent value) => new(value.Id, value.Action, value.Reference, value.User, value.At);
}

public sealed record BasketReturnSnapshot(
    Guid Id,
    string BasketNumber,
    int ContentSequence,
    string SourceLocation,
    string TargetLocation,
    DateTime StartedAt,
    string StartedBy,
    DateTime? ArrivedAt,
    string? ArrivedBy,
    Guid Version)
{
    public static BasketReturnSnapshot From(BasketReturn value) => new(value.Id, value.BasketNumber, value.ContentSequence, value.SourceLocation, value.TargetLocation, value.StartedAt, value.StartedBy, value.ArrivedAt, value.ArrivedBy, value.Version);
}

public sealed record MaterialLotSnapshot(
    string Number,
    string BatchNumber,
    string Material)
{
    public static MaterialLotSnapshot From(MaterialLot value) => new(value.Number, value.BatchNumber, value.Material);
}

public sealed record PlantLocationSnapshot(
    string Id,
    StationKind Kind,
    int Capacity)
{
    public static PlantLocationSnapshot From(PlantLocation value) => new(value.Id, value.Kind, value.Capacity);
}

public sealed record TransportOrderSnapshot(
    int Id,
    int BasketContentSequence,
    string OrderNumber,
    string HeatNumber,
    string PieceNumber,
    string? ParentPieceNumber,
    string Material,
    int Quantity,
    decimal Weight,
    decimal LoadingLength,
    string SourceLocation,
    string TargetLocation,
    OrderPriority Priority,
    DateTime DueAt,
    OrderStatus Status,
    string? TransportNumber,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<StatusChangeSnapshot> History)
{
    public static TransportOrderSnapshot From(TransportOrder value) => new(value.Id, value.BasketContentSequence, value.OrderNumber, value.HeatNumber, value.PieceNumber, value.ParentPieceNumber, value.Material, value.Quantity, value.Weight, value.LoadingLength, value.SourceLocation, value.TargetLocation, value.Priority, value.DueAt, value.Status, value.TransportNumber, value.CreatedAt, value.UpdatedAt, value.History.Select(StatusChangeSnapshot.From).ToArray());
}

public sealed record TransportSnapshot(
    int Id,
    string TransportNumber,
    VehicleType Vehicle,
    decimal LoadingLength,
    decimal Payload,
    decimal LoadedLength,
    decimal LoadedWeight,
    int OrderCount,
    string SourceLocation,
    string TargetLocation,
    TransportStatus Status,
    PartLoadReason? PartLoadReason,
    DateTime DispatchedAt,
    string DispatchedBy,
    DateTime? CompletedAt)
{
    public static TransportSnapshot From(Transport value) => new(value.Id, value.TransportNumber, value.Vehicle, value.LoadingLength, value.Payload, value.LoadedLength, value.LoadedWeight, value.OrderCount, value.SourceLocation, value.TargetLocation, value.Status, value.PartLoadReason, value.DispatchedAt, value.DispatchedBy, value.CompletedAt);
}

public sealed record StatusChangeSnapshot(
    int Id,
    OrderStatus? FromStatus,
    OrderStatus ToStatus,
    DateTime ChangedAt,
    string ChangedBy,
    string? Note)
{
    public static StatusChangeSnapshot From(StatusChange value) => new(value.Id, value.FromStatus, value.ToStatus, value.ChangedAt, value.ChangedBy, value.Note);
}

public sealed record EquipmentEventSnapshot(
    int Id,
    DateTime At,
    string By,
    string Description)
{
    public static EquipmentEventSnapshot From(EquipmentEvent value) => new(value.Id, value.At, value.By, value.Description);
}

public sealed record PlantEquipmentSnapshot(
    string Id,
    EquipmentKind Kind,
    string? LocationId,
    EquipmentMode Mode,
    EquipmentCondition Condition,
    string? ActiveOperation,
    Guid Version,
    IReadOnlyList<EquipmentEventSnapshot> History)
{
    public static PlantEquipmentSnapshot From(PlantEquipment value) => new(value.Id, value.Kind, value.LocationId, value.Mode, value.Condition, value.ActiveOperation, value.Version, value.History.Select(EquipmentEventSnapshot.From).ToArray());
}

public sealed record OvenRecipeSnapshot(
    string Id,
    string Alloy,
    decimal TargetTemperature,
    int HeatingMinutes,
    int HoldingMinutes,
    int CoolingMinutes, int Revision = 1, Guid Version = default, RecipeStatus Status = RecipeStatus.Validated,
    IReadOnlyList<RecipeEventSnapshot>? History = null)
{
    public static OvenRecipeSnapshot From(OvenRecipe value) => new(value.Id, value.Alloy, value.TargetTemperature, value.HeatingMinutes, value.HoldingMinutes, value.CoolingMinutes,
        value.Revision, value.Version, value.Status, value.History.OrderBy(e => e.Id).Select(e => e.Snapshot()).ToArray());
}

public sealed record OvenCycleLoadSnapshot(
    Guid CycleId,
    string BasketNumber,
    string MaterialLotNumber,
    int ContentSequence)
{
    public static OvenCycleLoadSnapshot From(OvenCycleLoad value) => new(value.CycleId, value.BasketNumber, value.MaterialLotNumber, value.ContentSequence);
}

public sealed record OvenCycleSnapshot(
    Guid Id,
    string EquipmentId,
    string RecipeId,
    decimal TargetTemperature,
    int HeatingMinutes,
    int HoldingMinutes,
    int CoolingMinutes,
    int ElapsedMinutes,
    OvenPhase Phase,
    DateTime StartedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    Guid Version,
    IReadOnlyList<OvenCycleLoadSnapshot> Load, int RecipeRevision = 1)
{
    public static OvenCycleSnapshot From(OvenCycle value) => new(value.Id, value.EquipmentId, value.RecipeId, value.TargetTemperature, value.HeatingMinutes, value.HoldingMinutes, value.CoolingMinutes, value.ElapsedMinutes, value.Phase, value.StartedAt, value.UpdatedAt, value.CompletedAt, value.Version, value.Load.Select(OvenCycleLoadSnapshot.From).ToArray(), value.RecipeRevision);
}

public sealed record PackageEventSnapshot(
    int Id,
    PackageStatus Status,
    string User,
    string Reason,
    DateTime At)
{
    public static PackageEventSnapshot From(PackageEvent value) => new(value.Id, value.Status, value.User, value.Reason, value.At);
}

public sealed record PackedPackageSnapshot(
    string Number,
    string SourceBasketNumber,
    int ContentSequence,
    string MaterialLotNumber,
    string BatchNumber,
    string Material,
    int Quantity,
    decimal Weight,
    decimal LoadingLength,
    string EquipmentId,
    string? LocationId,
    string? ReservedLocationId,
    PackageStatus Status,
    bool? VisualPassed,
    bool? QuantityVerified,
    string? LabelText,
    DateTime CreatedAt,
    DateTime? ShippedAt,
    Guid Version,
    IReadOnlyList<PackageEventSnapshot> History)
{
    public static PackedPackageSnapshot From(PackedPackage value) => new(value.Number, value.SourceBasketNumber, value.ContentSequence, value.MaterialLotNumber, value.BatchNumber, value.Material, value.Quantity, value.Weight, value.LoadingLength, value.EquipmentId, value.LocationId, value.ReservedLocationId, value.Status, value.VisualPassed, value.QuantityVerified, value.LabelText, value.CreatedAt, value.ShippedAt, value.Version, value.History.Select(PackageEventSnapshot.From).ToArray());
}

public sealed record PlcEventSnapshot(
    int Id,
    DateTime At,
    string Actor,
    string Detail)
{
    public static PlcEventSnapshot From(PlcEvent value) => new(value.Id, value.At, value.Actor, value.Detail);
}

public sealed record PlcCommandSnapshot(
    Guid Id,
    int ContractVersion,
    PlcCommandKind Kind,
    string Operation,
    string EquipmentId,
    string Source,
    string Target,
    string Payload,
    string PayloadHash,
    PlcCommandStatus Status,
    int Attempts,
    int LastSequence,
    DateTime CreatedAt,
    DateTime? LastAttemptAt,
    DateTime? DeadlineAt,
    DateTime? AppliedAt,
    string? AppliedBy,
    string? LastError,
    Guid Version,
    IReadOnlyList<PlcEventSnapshot> History)
{
    public static PlcCommandSnapshot From(PlcCommand value) => new(value.Id, value.ContractVersion, value.Kind, value.Operation, value.EquipmentId, value.Source, value.Target, value.Payload, value.PayloadHash, value.Status, value.Attempts, value.LastSequence, value.CreatedAt, value.LastAttemptAt, value.DeadlineAt, value.AppliedAt, value.AppliedBy, value.LastError, value.Version, value.History.Select(PlcEventSnapshot.From).ToArray());
}

public sealed record PlcSimulationSettingsSnapshot(
    int Id,
    bool Connected,
    bool SuppressAcknowledgements,
    Guid Version,
    string ChangedBy,
    string Reason,
    DateTime ChangedAt)
{
    public static PlcSimulationSettingsSnapshot From(PlcSimulationSettings value) => new(value.Id, value.Connected, value.SuppressAcknowledgements, value.Version, value.ChangedBy, value.Reason, value.ChangedAt);
}
