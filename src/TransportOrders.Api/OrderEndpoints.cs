using Microsoft.AspNetCore.Http.HttpResults;
using TransportOrders.Core;

namespace TransportOrders.Api;

/// <summary>
/// Read-only endpoints. Status changes stay in the desktop app, where the domain rules are applied;
/// the API exposes no write operation that could bypass them.
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapGet("/", GetOrdersAsync)
            .WithName("GetOrders")
            .WithSummary("List orders, optionally filtered by status, heat number or overdue flag.");

        orders.MapGet("/{id:int}", GetOrderAsync)
            .WithName("GetOrder")
            .WithSummary("Get one order with its status history.");

        orders.MapGet("/{id:int}/history", GetHistoryAsync)
            .WithName("GetOrderHistory")
            .WithSummary("Get the audit trail of one order: every status, when and by whom.");

        orders.MapGet("/by-number/{orderNumber}", GetByNumberAsync)
            .WithName("GetOrderByNumber")
            .WithSummary("Look an order up by its number (case-insensitive), e.g. from a scanned barcode.");

        app.MapGet("/api/shift-summary", GetShiftSummaryAsync)
            .WithTags("Shift")
            .WithName("GetShiftSummary")
            .WithSummary("Key figures of the current shift (Early 06-14, Late 14-22, Night 22-06, plant time).");

        return app;
    }

    private static async Task<Results<Ok<List<OrderDto>>, ValidationProblem>> GetOrdersAsync(
        string? status, string? heat, bool? overdue,
        IOrderRepository repository, TimeProvider clock, CancellationToken cancellationToken)
    {
        OrderStatus? statusFilter = null;
        if (status is not null)
        {
            if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"Unknown status '{status}'. Use Planned, InTransit, Done or Cancelled."],
                });
            }

            statusFilter = parsed;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var result = (await repository.GetAllAsync(cancellationToken))
            .Where(o => statusFilter is null || o.Status == statusFilter)
            .Where(o => string.IsNullOrWhiteSpace(heat) || string.Equals(o.HeatNumber, heat.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(o => overdue is null || o.IsOverdue(now) == overdue)
            .Select(o => OrderDto.From(o, now))
            .ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<OrderDetailDto>, NotFound>> GetOrderAsync(
        int id, IOrderRepository repository, TimeProvider clock, CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(id, cancellationToken) is { } order
            ? TypedResults.Ok(OrderDetailDto.From(order, clock.GetUtcNow().UtcDateTime))
            : TypedResults.NotFound();

    private static async Task<Results<Ok<List<StatusChangeDto>>, NotFound>> GetHistoryAsync(
        int id, IOrderRepository repository, CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(id, cancellationToken) is { } order
            ? TypedResults.Ok(order.History.Select(StatusChangeDto.From).ToList())
            : TypedResults.NotFound();

    private static async Task<Results<Ok<OrderDetailDto>, NotFound>> GetByNumberAsync(
        string orderNumber, IOrderRepository repository, TimeProvider clock, CancellationToken cancellationToken) =>
        await repository.GetByOrderNumberAsync(orderNumber, cancellationToken) is { } order
            ? TypedResults.Ok(OrderDetailDto.From(order, clock.GetUtcNow().UtcDateTime))
            : TypedResults.NotFound();

    private static async Task<Ok<ShiftSummaryDto>> GetShiftSummaryAsync(
        IOrderRepository repository, TimeProvider clock, PlantTimeZone plant, CancellationToken cancellationToken)
    {
        var orders = await repository.GetAllAsync(cancellationToken);
        var summary = ShiftSummary.Calculate(orders, clock.GetUtcNow().UtcDateTime, plant.Zone);
        return TypedResults.Ok(ShiftSummaryDto.From(summary, plant.Zone));
    }
}
