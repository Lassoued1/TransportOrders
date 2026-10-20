namespace TransportOrders.Core;

// Restores read-side materialized state without replaying or inventing domain events.

public sealed partial class Basket
{
    internal static Basket FromSnapshot(BasketSnapshot snapshot)
    {
        var value = new Basket { Number = snapshot.Number, MaterialLotNumber = snapshot.MaterialLotNumber, ContentSequence = snapshot.ContentSequence, Quantity = snapshot.Quantity, Weight = snapshot.Weight, LoadingLength = snapshot.LoadingLength, LocationId = snapshot.LocationId, ReservedLocationId = snapshot.ReservedLocationId, ActiveTransportNumber = snapshot.ActiveTransportNumber, ActiveReturnId = snapshot.ActiveReturnId, Version = snapshot.Version };
        value._history.AddRange(snapshot.History.Select(BasketEvent.FromSnapshot));
        return value;
    }
}

public sealed partial class BasketEvent
{
    internal static BasketEvent FromSnapshot(BasketEventSnapshot snapshot)
    {
        var value = new BasketEvent { Id = snapshot.Id, Action = snapshot.Action, Reference = snapshot.Reference, User = snapshot.User, At = snapshot.At };

        return value;
    }
}

public sealed partial class BasketReturn
{
    internal static BasketReturn FromSnapshot(BasketReturnSnapshot snapshot)
    {
        var value = new BasketReturn { Id = snapshot.Id, BasketNumber = snapshot.BasketNumber, ContentSequence = snapshot.ContentSequence, SourceLocation = snapshot.SourceLocation, TargetLocation = snapshot.TargetLocation, StartedAt = snapshot.StartedAt, StartedBy = snapshot.StartedBy, ArrivedAt = snapshot.ArrivedAt, ArrivedBy = snapshot.ArrivedBy, Version = snapshot.Version };

        return value;
    }
}

public sealed partial class MaterialLot
{
    internal static MaterialLot FromSnapshot(MaterialLotSnapshot snapshot)
    {
        var value = new MaterialLot { Number = snapshot.Number, BatchNumber = snapshot.BatchNumber, Material = snapshot.Material };

        return value;
    }
}

public sealed partial class PlantLocation
{
    internal static PlantLocation FromSnapshot(PlantLocationSnapshot snapshot)
    {
        var value = new PlantLocation { Id = snapshot.Id, Kind = snapshot.Kind, Capacity = snapshot.Capacity };

        return value;
    }
}

public sealed partial class TransportOrder
{
    internal static TransportOrder FromSnapshot(TransportOrderSnapshot snapshot)
    {
        var value = new TransportOrder { Id = snapshot.Id, BasketContentSequence = snapshot.BasketContentSequence, OrderNumber = snapshot.OrderNumber, HeatNumber = snapshot.HeatNumber, PieceNumber = snapshot.PieceNumber, ParentPieceNumber = snapshot.ParentPieceNumber, Material = snapshot.Material, Quantity = snapshot.Quantity, Weight = snapshot.Weight, LoadingLength = snapshot.LoadingLength, SourceLocation = snapshot.SourceLocation, TargetLocation = snapshot.TargetLocation, Priority = snapshot.Priority, DueAt = snapshot.DueAt, Status = snapshot.Status, TransportNumber = snapshot.TransportNumber, CreatedAt = snapshot.CreatedAt, UpdatedAt = snapshot.UpdatedAt };
        value._history.AddRange(snapshot.History.Select(StatusChange.FromSnapshot));
        return value;
    }
}

public sealed partial class Transport
{
    internal static Transport FromSnapshot(TransportSnapshot snapshot)
    {
        var value = new Transport { Id = snapshot.Id, TransportNumber = snapshot.TransportNumber, Vehicle = snapshot.Vehicle, LoadingLength = snapshot.LoadingLength, Payload = snapshot.Payload, LoadedLength = snapshot.LoadedLength, LoadedWeight = snapshot.LoadedWeight, OrderCount = snapshot.OrderCount, SourceLocation = snapshot.SourceLocation, TargetLocation = snapshot.TargetLocation, Status = snapshot.Status, PartLoadReason = snapshot.PartLoadReason, DispatchedAt = snapshot.DispatchedAt, DispatchedBy = snapshot.DispatchedBy, CompletedAt = snapshot.CompletedAt };

        return value;
    }
}

public sealed partial class StatusChange
{
    internal static StatusChange FromSnapshot(StatusChangeSnapshot snapshot)
    {
        var value = new StatusChange { Id = snapshot.Id, FromStatus = snapshot.FromStatus, ToStatus = snapshot.ToStatus, ChangedAt = snapshot.ChangedAt, ChangedBy = snapshot.ChangedBy, Note = snapshot.Note };

        return value;
    }
}

public sealed partial class EquipmentEvent
{
    internal static EquipmentEvent FromSnapshot(EquipmentEventSnapshot snapshot)
    {
        var value = new EquipmentEvent { Id = snapshot.Id, At = snapshot.At, By = snapshot.By, Description = snapshot.Description };

        return value;
    }
}

public sealed partial class PlantEquipment
{
    internal static PlantEquipment FromSnapshot(PlantEquipmentSnapshot snapshot)
    {
        var value = new PlantEquipment { Id = snapshot.Id, Kind = snapshot.Kind, LocationId = snapshot.LocationId, Mode = snapshot.Mode, Condition = snapshot.Condition, ActiveOperation = snapshot.ActiveOperation, Version = snapshot.Version };
        value._history.AddRange(snapshot.History.Select(EquipmentEvent.FromSnapshot));
        return value;
    }
}

public sealed partial class OvenRecipe
{
    internal static OvenRecipe FromSnapshot(OvenRecipeSnapshot snapshot)
    {
        var value = new OvenRecipe { Id = snapshot.Id, Alloy = snapshot.Alloy, TargetTemperature = snapshot.TargetTemperature, HeatingMinutes = snapshot.HeatingMinutes, HoldingMinutes = snapshot.HoldingMinutes, CoolingMinutes = snapshot.CoolingMinutes,
            Revision = snapshot.Revision, Version = snapshot.Version, Status = snapshot.Status };
        value._history.AddRange((snapshot.History ?? []).Select(RecipeEvent.Restore));
        return value;
    }
}

public sealed partial class OvenCycleLoad
{
    internal static OvenCycleLoad FromSnapshot(OvenCycleLoadSnapshot snapshot)
    {
        var value = new OvenCycleLoad { CycleId = snapshot.CycleId, BasketNumber = snapshot.BasketNumber, MaterialLotNumber = snapshot.MaterialLotNumber, ContentSequence = snapshot.ContentSequence };

        return value;
    }
}

public sealed partial class OvenCycle
{
    internal static OvenCycle FromSnapshot(OvenCycleSnapshot snapshot)
    {
        var value = new OvenCycle { Id = snapshot.Id, EquipmentId = snapshot.EquipmentId, RecipeId = snapshot.RecipeId, RecipeRevision = snapshot.RecipeRevision, TargetTemperature = snapshot.TargetTemperature, HeatingMinutes = snapshot.HeatingMinutes, HoldingMinutes = snapshot.HoldingMinutes, CoolingMinutes = snapshot.CoolingMinutes, ElapsedMinutes = snapshot.ElapsedMinutes, Phase = snapshot.Phase, StartedAt = snapshot.StartedAt, UpdatedAt = snapshot.UpdatedAt, CompletedAt = snapshot.CompletedAt, Version = snapshot.Version };
        value._load.AddRange(snapshot.Load.Select(OvenCycleLoad.FromSnapshot));
        return value;
    }
}

public sealed partial class PackageEvent
{
    internal static PackageEvent FromSnapshot(PackageEventSnapshot snapshot)
    {
        var value = new PackageEvent { Id = snapshot.Id, Status = snapshot.Status, User = snapshot.User, Reason = snapshot.Reason, At = snapshot.At };

        return value;
    }
}

public sealed partial class PackedPackage
{
    internal static PackedPackage FromSnapshot(PackedPackageSnapshot snapshot)
    {
        var value = new PackedPackage { Number = snapshot.Number, SourceBasketNumber = snapshot.SourceBasketNumber, ContentSequence = snapshot.ContentSequence, MaterialLotNumber = snapshot.MaterialLotNumber, BatchNumber = snapshot.BatchNumber, Material = snapshot.Material, Quantity = snapshot.Quantity, Weight = snapshot.Weight, LoadingLength = snapshot.LoadingLength, EquipmentId = snapshot.EquipmentId, LocationId = snapshot.LocationId, ReservedLocationId = snapshot.ReservedLocationId, Status = snapshot.Status, VisualPassed = snapshot.VisualPassed, QuantityVerified = snapshot.QuantityVerified, LabelText = snapshot.LabelText, CreatedAt = snapshot.CreatedAt, ShippedAt = snapshot.ShippedAt, Version = snapshot.Version };
        value._history.AddRange(snapshot.History.Select(PackageEvent.FromSnapshot));
        return value;
    }
}

public sealed partial class PlcEvent
{
    internal static PlcEvent FromSnapshot(PlcEventSnapshot snapshot)
    {
        var value = new PlcEvent { Id = snapshot.Id, At = snapshot.At, Actor = snapshot.Actor, Detail = snapshot.Detail };

        return value;
    }
}

public sealed partial class PlcCommand
{
    internal static PlcCommand FromSnapshot(PlcCommandSnapshot snapshot)
    {
        var value = new PlcCommand { Id = snapshot.Id, ContractVersion = snapshot.ContractVersion, Kind = snapshot.Kind, Operation = snapshot.Operation, EquipmentId = snapshot.EquipmentId, Source = snapshot.Source, Target = snapshot.Target, Payload = snapshot.Payload, PayloadHash = snapshot.PayloadHash, Status = snapshot.Status, Attempts = snapshot.Attempts, LastSequence = snapshot.LastSequence, CreatedAt = snapshot.CreatedAt, LastAttemptAt = snapshot.LastAttemptAt, DeadlineAt = snapshot.DeadlineAt, AppliedAt = snapshot.AppliedAt, AppliedBy = snapshot.AppliedBy, LastError = snapshot.LastError, Version = snapshot.Version };
        value._history.AddRange(snapshot.History.Select(PlcEvent.FromSnapshot));
        return value;
    }
}

public sealed partial class PlcSimulationSettings
{
    internal static PlcSimulationSettings FromSnapshot(PlcSimulationSettingsSnapshot snapshot)
    {
        var value = new PlcSimulationSettings { Id = snapshot.Id, Connected = snapshot.Connected, SuppressAcknowledgements = snapshot.SuppressAcknowledgements, Version = snapshot.Version, ChangedBy = snapshot.ChangedBy, Reason = snapshot.Reason, ChangedAt = snapshot.ChangedAt };

        return value;
    }
}
