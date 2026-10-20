namespace TransportOrders.Core;

/// <summary>A physical basket with one loaded lot and an authoritative persisted position.</summary>
public sealed partial class Basket
{
    private readonly List<BasketEvent> _history = [];
    private Basket() { }
    public Basket(string number, string materialLotNumber, int quantity, decimal weight, decimal loadingLength, string locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialLotNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (weight <= 0 || weight > TransportOrder.MaxWeight) throw new ArgumentOutOfRangeException(nameof(weight));
        if (loadingLength <= 0 || loadingLength > TransportOrder.MaxLoadingLength) throw new ArgumentOutOfRangeException(nameof(loadingLength));
        Number = number.Trim();
        MaterialLotNumber = materialLotNumber.Trim();
        Quantity = quantity;
        Weight = weight;
        LoadingLength = loadingLength;
        LocationId = locationId.Trim();
        Version = Guid.NewGuid();
    }
    public string Number { get; private set; } = string.Empty;
    public string MaterialLotNumber { get; private set; } = string.Empty;
    public string? CurrentMaterialLotNumber => IsEmpty ? null : MaterialLotNumber;
    public int ContentSequence { get; private set; } = 1;
    public bool IsEmpty => Quantity == 0;
    public IReadOnlyList<BasketEvent> History => _history.AsReadOnly();
    public int Quantity { get; private set; }
    public decimal Weight { get; private set; }
    public decimal LoadingLength { get; private set; }
    public string? LocationId { get; private set; }
    public string? ReservedLocationId { get; private set; }
    public string? ActiveTransportNumber { get; private set; }
    public Guid? ActiveReturnId { get; private set; }
    public Guid Version { get; private set; }
    public bool IsMoving => ActiveTransportNumber is not null || ActiveReturnId is not null;

    public void Unload(string packageNumber, string user, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageNumber);
        if (IsEmpty || IsMoving) throw new TransportRuleException("Only a stationary loaded basket can be unloaded.");
        Record("Unloaded", packageNumber, user, now);
        Quantity = 0; Weight = 0.15m; // Illustrative empty basket tare; physical loading length is retained.
    }
    public void Reload(MaterialLot lot, int quantity, decimal weight, decimal loadingLength, string user, DateTime now)
    {
        if (!IsEmpty || IsMoving) throw new TransportRuleException("Only a stationary empty basket can be reloaded.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (weight <= 0 || weight > TransportOrder.MaxWeight) throw new ArgumentOutOfRangeException(nameof(weight));
        if (loadingLength <= 0 || loadingLength > TransportOrder.MaxLoadingLength) throw new ArgumentOutOfRangeException(nameof(loadingLength));
        Record("Reloaded", lot.Number, user, now);
        ContentSequence = checked(ContentSequence + 1);
        MaterialLotNumber = lot.Number; Quantity = quantity; Weight = weight; LoadingLength = loadingLength;
    }
    public void DepartEmpty(BasketReturn movement, string user, DateTime now)
    {
        if (!IsEmpty || IsMoving || LocationId != movement.SourceLocation || Number != movement.BasketNumber
            || ContentSequence != movement.ContentSequence || movement.ArrivedAt is not null)
            throw new TransportRuleException("An empty return must match this available basket.");
        Record("Return started", movement.Id.ToString(), user, now);
        LocationId = null; ReservedLocationId = movement.TargetLocation; ActiveReturnId = movement.Id;
    }
    public void ArriveEmpty(BasketReturn movement, string user, DateTime now)
    {
        if (!IsEmpty || ActiveReturnId != movement.Id || ReservedLocationId != movement.TargetLocation || ContentSequence != movement.ContentSequence)
            throw new TransportRuleException("The empty return acknowledgement does not match the basket.");
        Record("Return arrived", movement.Id.ToString(), user, now);
        LocationId = ReservedLocationId; ReservedLocationId = null; ActiveReturnId = null;
    }
    private void Record(string action, string reference, string user, DateTime now)
    {
        _history.Add(new(action, reference, user, now)); Version = Guid.NewGuid();
    }

    public void Depart(string source, string destination, string transportNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(transportNumber);
        if (IsEmpty || IsMoving || LocationId != source)
            throw new TransportRuleException($"Basket {Number} is not available at {source}.");
        if (source == destination) throw new TransportRuleException("A transfer must change location.");
        LocationId = null;
        ReservedLocationId = destination;
        ActiveTransportNumber = transportNumber;
        Version = Guid.NewGuid();
    }

    public void Arrive(string transportNumber)
    {
        if (!IsMoving || ActiveTransportNumber != transportNumber || ReservedLocationId is null)
            throw new TransportRuleException($"Arrival {transportNumber} does not match basket {Number}'s active transfer.");
        LocationId = ReservedLocationId;
        ReservedLocationId = null;
        ActiveTransportNumber = null;
        Version = Guid.NewGuid();
    }
}
