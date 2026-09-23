using TransportOrders.Core;

namespace TransportOrders.Api;

// Response contracts: the API never serializes EF entities directly, so the domain can evolve independently.

public sealed record OrderDto(
    int Id,
    string OrderNumber,
    string HeatNumber,
    string Material,
    int Quantity,
    string Unit,
    string SourceLocation,
    string TargetLocation,
    OrderStatus Status,
    OrderPriority Priority,
    DateTime DueAt,
    bool IsOverdue,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static OrderDto From(TransportOrder order, DateTime utcNow) => new(
        order.Id, order.OrderNumber, order.HeatNumber, order.Material, order.Quantity, "pcs",
        order.SourceLocation, order.TargetLocation, order.Status, order.Priority, order.DueAt,
        order.IsOverdue(utcNow), order.CreatedAt, order.UpdatedAt);
}

public sealed record StatusChangeDto(OrderStatus? FromStatus, OrderStatus ToStatus, DateTime ChangedAt, string ChangedBy)
{
    public static StatusChangeDto From(StatusChange change) =>
        new(change.FromStatus, change.ToStatus, change.ChangedAt, change.ChangedBy);
}

public sealed record OrderDetailDto(OrderDto Order, IReadOnlyList<StatusChangeDto> History)
{
    public static OrderDetailDto From(TransportOrder order, DateTime utcNow) =>
        new(OrderDto.From(order, utcNow), order.History.Select(StatusChangeDto.From).ToList());
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
    int OpenOrders,
    int OverdueOrders,
    int UrgentOpenOrders)
{
    public static ShiftSummaryDto From(ShiftSummary summary, TimeZoneInfo zone) => new(
        summary.Shift.Kind, summary.Shift.StartLocal, summary.Shift.EndLocal, zone.Id,
        summary.Started, summary.Completed, summary.Cancelled,
        summary.AverageTransitTime is { } average ? Math.Round(average.TotalMinutes, 1) : null,
        summary.OpenOrders, summary.OverdueOrders, summary.UrgentOpenOrders);
}
