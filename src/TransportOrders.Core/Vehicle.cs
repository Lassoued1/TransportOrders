namespace TransportOrders.Core;

public enum VehicleType
{
    Forklift,
    HeavyLoadCarrier,
    RailWagon,
    BasketShuttle,
    OverheadCrane
}

/// <summary>A vehicle type: its loading length in metres (what "full" is measured on) and its payload in tonnes (a hard limit).</summary>
public sealed record Vehicle(VehicleType Type, decimal LoadingLength, decimal Payload);

public static class Fleet
{
    public static IReadOnlyList<Vehicle> Extrusion { get; } =
    [
        new(VehicleType.BasketShuttle, 7m, 4m),
        new(VehicleType.OverheadCrane, 14m, 8m),
    ];

    /// <summary>Demo values (invented): heavy forklift, heavy-load carrier, internal rail wagon.</summary>
    public static IReadOnlyList<Vehicle> Default { get; } =
    [
        new(VehicleType.Forklift, 4m, 8m),
        new(VehicleType.HeavyLoadCarrier, 12m, 40m),
        new(VehicleType.RailWagon, 18m, 60m),
    ];
}

/// <summary>Why a transport left with less than the full-load share of its loading length.</summary>
public enum PartLoadReason
{
    UrgentMaterial,

    /// <summary>The vehicle cannot carry more weight although its loading length is not used up (short, heavy pieces).</summary>
    PayloadLimit,

    EndOfShift,
    NoMatchingMaterial
}
