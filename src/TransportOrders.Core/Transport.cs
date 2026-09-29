using System.Text.RegularExpressions;

namespace TransportOrders.Core;

public enum TransportStatus
{
    InTransit,
    Done
}

/// <summary>
/// One trip of one vehicle on one route, carrying several orders. The loading rules live here:
/// never empty, one route per trip, never longer than the loading bed or heavier than the payload,
/// and at least <see cref="FullLoadThreshold"/> of the loading length used unless a part-load reason is recorded.
/// </summary>
public sealed partial class Transport
{
    /// <summary>A load counts as full from 90 % of the vehicle's loading length.</summary>
    public const decimal FullLoadThreshold = 0.90m;

    // Used by EF Core.
    private Transport()
    {
    }

    public int Id { get; private set; }

    public string TransportNumber { get; private set; } = string.Empty;

    public VehicleType Vehicle { get; private set; }

    /// <summary>Loading length of the vehicle in metres, as dispatched.</summary>
    public decimal LoadingLength { get; private set; }

    /// <summary>Payload of the vehicle in tonnes, as dispatched.</summary>
    public decimal Payload { get; private set; }

    /// <summary>Loading length used, in metres.</summary>
    public decimal LoadedLength { get; private set; }

    /// <summary>Total weight loaded, in tonnes.</summary>
    public decimal LoadedWeight { get; private set; }

    public int OrderCount { get; private set; }

    public string SourceLocation { get; private set; } = string.Empty;

    public string TargetLocation { get; private set; } = string.Empty;

    public TransportStatus Status { get; private set; }

    public PartLoadReason? PartLoadReason { get; private set; }

    public DateTime DispatchedAt { get; private set; }

    public string DispatchedBy { get; private set; } = string.Empty;

    public DateTime? CompletedAt { get; private set; }

    /// <summary>Share of the loading length in use: the measure of a full load.</summary>
    public decimal FillRate => LoadingLength == 0 ? 0 : LoadedLength / LoadingLength;

    public bool IsPartLoad => FillRate < FullLoadThreshold;

    /// <summary>Loads <paramref name="orders"/> on <paramref name="vehicle"/> and starts moving them.</summary>
    /// <exception cref="TransportRuleException">Empty trip, mixed routes, too long, too heavy, or material that cannot be moved.</exception>
    /// <exception cref="PartLoadNotJustifiedException">Below the full-load threshold without a reason.</exception>
    public static Transport Dispatch(
        string transportNumber,
        Vehicle vehicle,
        IReadOnlyCollection<TransportOrder> orders,
        string dispatchedBy,
        DateTime utcNow,
        PartLoadReason? partLoadReason = null)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(orders);
        if (string.IsNullOrWhiteSpace(transportNumber) || !TransportNumberPattern().IsMatch(transportNumber))
        {
            throw new ArgumentException("Transport number must follow the format TR-YYYY-NNNN.", nameof(transportNumber));
        }

        if (string.IsNullOrWhiteSpace(dispatchedBy))
        {
            throw new ArgumentException("Value is required.", nameof(dispatchedBy));
        }

        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Date must be expressed in UTC.", nameof(utcNow));
        }

        if (orders.Count == 0)
        {
            throw new TransportRuleException("A transport cannot leave empty.");
        }

        if (orders.Select(o => o.OrderNumber).Distinct().Count() != orders.Count)
        {
            throw new TransportRuleException("An order can only be loaded once.");
        }

        if (orders.Select(o => (o.SourceLocation, o.TargetLocation)).Distinct().Count() > 1)
        {
            throw new TransportRuleException("All orders of a transport must share the same route.");
        }

        if (orders.FirstOrDefault(o => o.Status != OrderStatus.Planned || o.TransportNumber is not null) is { } blocked)
        {
            throw new TransportRuleException($"Order {blocked.OrderNumber} is {blocked.Status} and cannot be loaded.");
        }

        var length = orders.Sum(o => o.LoadingLength);
        if (length > vehicle.LoadingLength)
        {
            throw new TransportRuleException($"{length} m exceeds the loading length of the {vehicle.Type} ({vehicle.LoadingLength} m).");
        }

        var weight = orders.Sum(o => o.Weight);
        if (weight > vehicle.Payload)
        {
            throw new TransportRuleException($"{weight} t exceeds the payload of the {vehicle.Type} ({vehicle.Payload} t).");
        }

        var fillRate = length / vehicle.LoadingLength;
        if (fillRate < FullLoadThreshold && partLoadReason is null)
        {
            throw new PartLoadNotJustifiedException(fillRate);
        }

        var first = orders.First();
        var transport = new Transport
        {
            TransportNumber = transportNumber,
            Vehicle = vehicle.Type,
            LoadingLength = vehicle.LoadingLength,
            Payload = vehicle.Payload,
            LoadedLength = length,
            LoadedWeight = weight,
            OrderCount = orders.Count,
            SourceLocation = first.SourceLocation,
            TargetLocation = first.TargetLocation,
            Status = TransportStatus.InTransit,
            PartLoadReason = fillRate < FullLoadThreshold ? partLoadReason : null,
            DispatchedAt = utcNow,
            DispatchedBy = dispatchedBy.Trim(),
        };

        foreach (var order in orders)
        {
            order.Load(transportNumber, transport.DispatchedBy, utcNow);
        }

        return transport;
    }

    /// <summary>The transport arrived: every order still on it is marked Done (cancelled ones stay cancelled).</summary>
    public void Complete(IReadOnlyCollection<TransportOrder> orders, string completedBy, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if (Status != TransportStatus.InTransit)
        {
            throw new TransportRuleException($"Transport {TransportNumber} has already arrived.");
        }

        if (orders.Any(o => o.TransportNumber != TransportNumber) || orders.Count != OrderCount)
        {
            throw new TransportRuleException($"Pass exactly the {OrderCount} orders loaded on {TransportNumber}.");
        }

        foreach (var order in orders.Where(o => o.Status == OrderStatus.InTransit))
        {
            order.TransitionTo(OrderStatus.Done, completedBy, utcNow, TransportNumber);
        }

        Status = TransportStatus.Done;
        CompletedAt = utcNow;
    }

    [GeneratedRegex(@"^TR-\d{4}-\d{4}$")]
    private static partial Regex TransportNumberPattern();
}
