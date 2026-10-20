using System.Globalization;

namespace TransportOrders.Core;

public enum PackageStatus { Packing, AwaitingInspection, Rejected, Ready, InTransit, Shipped }

public sealed partial class PackageEvent
{
    private PackageEvent() { }
    public PackageEvent(PackageStatus status, string user, string reason, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user); ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = status; User = user; Reason = reason; At = now;
    }
    public int Id { get; private set; }
    public PackageStatus Status { get; private set; }
    public string User { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTime At { get; private set; }
}

/// <summary>One basket load becomes one labelled package; its material snapshot survives basket reuse.</summary>
public sealed partial class PackedPackage
{
    private readonly List<PackageEvent> _history = [];
    private PackedPackage() { }
    public PackedPackage(string number, Basket basket, MaterialLot lot, string equipmentId, string user, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (basket.IsEmpty || basket.IsMoving || basket.LocationId is null || basket.MaterialLotNumber != lot.Number)
            throw new TransportRuleException("Packing needs a stationary loaded basket and its current material lot.");
        Number = number; SourceBasketNumber = basket.Number; ContentSequence = basket.ContentSequence;
        MaterialLotNumber = lot.Number; BatchNumber = lot.BatchNumber; Material = lot.Material;
        Quantity = basket.Quantity; Weight = basket.Weight; LoadingLength = basket.LoadingLength;
        EquipmentId = equipmentId; LocationId = basket.LocationId; CreatedAt = now;
        Record(PackageStatus.Packing, user, "Packing started", now);
    }
    public string Number { get; private set; } = string.Empty;
    public string SourceBasketNumber { get; private set; } = string.Empty;
    public int ContentSequence { get; private set; }
    public string MaterialLotNumber { get; private set; } = string.Empty;
    public string BatchNumber { get; private set; } = string.Empty;
    public string Material { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal Weight { get; private set; }
    public decimal LoadingLength { get; private set; }
    public string EquipmentId { get; private set; } = string.Empty;
    public string? LocationId { get; private set; }
    public string? ReservedLocationId { get; private set; }
    public PackageStatus Status { get; private set; }
    public bool? VisualPassed { get; private set; }
    public bool? QuantityVerified { get; private set; }
    public string? LabelText { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public Guid Version { get; private set; }
    public IReadOnlyList<PackageEvent> History => _history.AsReadOnly();
    public void CompletePacking(string user, DateTime now)
    {
        if (Status != PackageStatus.Packing) throw new TransportRuleException("Packing is not active.");
        Record(PackageStatus.AwaitingInspection, user, "Visual and quantity checks required", now);
    }
    public void Inspect(bool visualPassed, bool quantityVerified, string user, string reason, DateTime now)
    {
        if (Status is not (PackageStatus.AwaitingInspection or PackageStatus.Rejected))
            throw new TransportRuleException("Only an unapproved package can be inspected.");
        Record(visualPassed && quantityVerified ? PackageStatus.Ready : PackageStatus.Rejected, user, reason, now);
        VisualPassed = visualPassed; QuantityVerified = quantityVerified;
        if (Status == PackageStatus.Ready)
            LabelText = string.Create(CultureInfo.InvariantCulture,
                $"PACKAGE {Number}\nLOT {MaterialLotNumber}\nBATCH {BatchNumber}\nMATERIAL {Material}\nQUANTITY {Quantity}\nWEIGHT {Weight:0.###} t\nBASKET {SourceBasketNumber} / LOAD {ContentSequence}\nCHECKED {now:O}");
    }
    public void Dispatch(string destination, string user, DateTime now)
    {
        if (Status != PackageStatus.Ready || string.IsNullOrWhiteSpace(LabelText))
            throw new TransportRuleException("Only a checked, labelled package can be shipped.");
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        Record(PackageStatus.InTransit, user, "Shipping transfer started", now);
        LocationId = null; ReservedLocationId = destination;
    }
    public void Arrive(string user, DateTime now)
    {
        if (Status != PackageStatus.InTransit || ReservedLocationId is null) throw new TransportRuleException("No shipping transfer is active.");
        Record(PackageStatus.Shipped, user, "Arrived at shipping", now);
        LocationId = ReservedLocationId; ReservedLocationId = null; ShippedAt = now;
    }
    private void Record(PackageStatus status, string user, string reason, DateTime now)
    {
        _history.Add(new(status, user, reason, now)); Status = status; Version = Guid.NewGuid();
    }
}
