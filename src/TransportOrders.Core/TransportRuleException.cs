namespace TransportOrders.Core;

/// <summary>A transport rule was violated: empty trip, overload, mixed routes, or material that cannot be moved.</summary>
public class TransportRuleException(string message) : InvalidOperationException(message);

/// <summary>A load below the full-load threshold was dispatched without a recorded reason.</summary>
public sealed class PartLoadNotJustifiedException(decimal fillRate)
    : TransportRuleException($"The load is only {fillRate:P0} full. A part load needs a reason (at least {Transport.FullLoadThreshold:P0} is required otherwise).")
{
    public decimal FillRate { get; } = fillRate;
}
