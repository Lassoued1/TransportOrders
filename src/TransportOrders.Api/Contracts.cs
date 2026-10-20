using TransportOrders.Core;

namespace TransportOrders.Api;

// Response contracts: the API never serializes EF entities directly, so the domain can evolve independently.

public sealed record OrderDto(
    int Id,
    string OrderNumber,
    string HeatNumber,
    string PieceNumber,
    string? ParentPieceNumber,
    string Material,
    int Quantity,
    decimal WeightTonnes,
    decimal LoadingLengthMetres,
    string SourceLocation,
    string TargetLocation,
    OrderStatus Status,
    OrderPriority Priority,
    string? TransportNumber,
    string? HoldReason,
    DateTime DueAt,
    bool IsOverdue,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public int BasketContentSequence { get; init; }
    public string Revision { get; init; } = "";
    public static OrderDto From(TransportOrder order, DateTime utcNow) => new(
        order.Id, order.OrderNumber, order.HeatNumber, order.PieceNumber, order.ParentPieceNumber, order.Material,
        order.Quantity, order.Weight, order.LoadingLength, order.SourceLocation, order.TargetLocation, order.Status,
        order.Priority, order.TransportNumber, order.HoldReason, order.DueAt, order.IsOverdue(utcNow),
        order.CreatedAt, order.UpdatedAt) { BasketContentSequence = order.BasketContentSequence, Revision = OrderRevision.For(order) };
}

public sealed record StatusChangeDto(OrderStatus? FromStatus, OrderStatus ToStatus, DateTime ChangedAt, string ChangedBy, string? Note)
{
    public static StatusChangeDto From(StatusChange change) =>
        new(change.FromStatus, change.ToStatus, change.ChangedAt, change.ChangedBy, change.Note);
}

public sealed record OrderDetailDto(OrderDto Order, IReadOnlyList<StatusChangeDto> History)
{
    public static OrderDetailDto From(TransportOrder order, DateTime utcNow) =>
        new(OrderDto.From(order, utcNow), order.History.Select(StatusChangeDto.From).ToList());
}

public sealed record TransportDto(
    string TransportNumber,
    VehicleType Vehicle,
    string SourceLocation,
    string TargetLocation,
    TransportStatus Status,
    int OrderCount,
    decimal LoadedLengthMetres,
    decimal LoadingLengthMetres,
    decimal FillRate,
    decimal LoadedWeightTonnes,
    decimal PayloadTonnes,
    bool IsPartLoad,
    PartLoadReason? PartLoadReason,
    DateTime DispatchedAt,
    string DispatchedBy,
    DateTime? CompletedAt)
{
    public static TransportDto From(Transport t) => new(
        t.TransportNumber, t.Vehicle, t.SourceLocation, t.TargetLocation, t.Status, t.OrderCount,
        t.LoadedLength, t.LoadingLength, Math.Round(t.FillRate, 3), t.LoadedWeight, t.Payload,
        t.IsPartLoad, t.PartLoadReason, t.DispatchedAt, t.DispatchedBy, t.CompletedAt);
}

public sealed record LoadProposalDto(
    VehicleType Vehicle,
    string SourceLocation,
    string TargetLocation,
    IReadOnlyList<string> OrderNumbers,
    decimal LoadedLengthMetres,
    decimal LoadingLengthMetres,
    decimal FillRate,
    decimal LoadedWeightTonnes,
    decimal PayloadTonnes,
    bool IsFull,
    PartLoadReason? SuggestedReason);

/// <param name="FullLoadThreshold">Share of the loading length a transport must use to count as full.</param>
/// <param name="Unplannable">Orders that fit no vehicle.</param>
public sealed record LoadPlanDto(decimal FullLoadThreshold, IReadOnlyList<LoadProposalDto> Proposals, IReadOnlyList<string> Unplannable)
{
    public static LoadPlanDto From(LoadPlan plan) => new(
        Transport.FullLoadThreshold,
        plan.Proposals.Select(p => new LoadProposalDto(
            p.Vehicle.Type, p.SourceLocation, p.TargetLocation, p.Orders.Select(o => o.OrderNumber).ToList(),
            p.LoadedLength, p.Vehicle.LoadingLength, Math.Round(p.FillRate, 3), p.LoadedWeight, p.Vehicle.Payload,
            p.IsFull, p.SuggestedReason)).ToList(),
        plan.Unplannable.Select(o => o.OrderNumber).ToList());
}

public sealed record PieceDto(string PieceNumber, string? Material, IReadOnlyList<string> OrderNumbers, IReadOnlyList<PieceDto> Children)
{
    public static PieceDto From(PieceNode node) => new(
        node.PieceNumber, node.Material, node.Orders.Select(o => o.OrderNumber).ToList(), node.Children.Select(From).ToList());
}

public sealed record GenealogyDto(string HeatNumber, int PieceCount, IReadOnlyList<PieceDto> Pieces)
{
    public static GenealogyDto From(HeatGenealogy genealogy) =>
        new(genealogy.HeatNumber, genealogy.PieceCount, genealogy.Roots.Select(PieceDto.From).ToList());
}

public sealed record ShiftSummaryDto(
    ShiftKind Shift,
    DateTime StartLocal,
    DateTime EndLocal,
    string TimeZone,
    int Started,
    int Completed,
    int Cancelled,
    double? AverageTransitMinutes,
    int Trips,
    decimal? AverageFillRate,
    int PartLoads,
    int OpenOrders,
    int OverdueOrders,
    int UrgentOpenOrders,
    int OnHoldOrders)
{
    public static ShiftSummaryDto From(ShiftSummary s, TimeZoneInfo zone) => new(
        s.Shift.Kind, s.Shift.StartLocal, s.Shift.EndLocal, zone.Id,
        s.Started, s.Completed, s.Cancelled,
        s.AverageTransitTime is { } average ? Math.Round(average.TotalMinutes, 1) : null,
        s.Trips, s.AverageFillRate is { } fill ? Math.Round(fill, 3) : null, s.PartLoads,
        s.OpenOrders, s.OverdueOrders, s.UrgentOpenOrders, s.OnHoldOrders);
}
