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

        app.MapGet("/api/transports", GetTransportsAsync)
            .WithTags("Transports")
            .WithName("GetTransports")
            .WithSummary("All transports, newest first, with their fill rate and any recorded part-load reason.");

        app.MapGet("/api/load-plan", GetLoadPlanAsync)
            .WithTags("Transports")
            .WithName("GetLoadPlan")
            .WithSummary("Proposed full loads for the orders waiting to move. A calculation only: nothing is dispatched.");

        app.MapGet("/api/plant", async (IOrderRepository repository, CancellationToken cancellationToken) =>
        {
            var state = await repository.GetPlantStateAsync(cancellationToken);
            var baskets = state.Locate();
            return TypedResults.Ok(new
            {
                MaterialLots = state.MaterialLots,
                Equipment = state.Equipment.Select(e => new { e.Id, e.Kind, e.LocationId, e.Status, e.Mode, e.Condition, e.ActiveOperation, e.Version, e.History }),
                Recipes = state.Recipes,
                Packages = state.Packages,
                EmptyReturns = state.Returns,
                OvenCycles = state.Cycles.Select(c => new { c.Id, c.EquipmentId, c.RecipeId, c.Phase, c.ElapsedMinutes,
                    c.TotalMinutes, c.SimulatedTemperature, c.TargetTemperature, c.StartedAt, c.UpdatedAt, c.CompletedAt, c.Load, c.Version }),
                Stations = state.Locations.Select(s => new
                {
                    s.Id, s.Kind, s.Capacity,
                    Occupied = state.Occupied(s.Id),
                    Reserved = state.Reserved(s.Id),
                }),
                Baskets = baskets.Select(b => new
                {
                    b.BasketNumber, MaterialLotNumber = b.Basket.CurrentMaterialLotNumber, LastMaterialLotNumber = b.Basket.MaterialLotNumber,
                    b.Basket.ContentSequence, b.Basket.IsEmpty, BatchNumber = b.Basket.IsEmpty ? null : b.MaterialLot.BatchNumber,
                    OrderNumber = b.Order?.OrderNumber, Material = b.Basket.IsEmpty ? null : b.MaterialLot.Material,
                    b.Basket.Quantity, b.Location, b.IsMoving, b.IsShipped,
                    b.Basket.ReservedLocationId, b.Basket.ActiveTransportNumber, b.Basket.ActiveReturnId, b.Basket.History,
                    SourceLocation = b.Order?.SourceLocation, TargetLocation = b.Order?.TargetLocation, Status = b.Order?.Status,
                }),
            });
        }).WithTags("Plant").WithSummary("Basket positions and occupied or reserved station capacity.");
        app.MapGet("/api/packages/{number}/label", async (string number, IOrderRepository repository, CancellationToken cancellationToken) =>
        {
            var package = (await repository.GetPlantStateAsync(cancellationToken)).Packages.FirstOrDefault(p => p.Number == number);
            return package?.LabelText is { } label ? (IResult)Results.Text(label, "text/plain; charset=utf-8") : Results.NotFound();
        }).WithTags("Packing").WithSummary("Approved package label as printable text; unapproved or unknown packages return 404.");
        app.MapGet("/api/schedule", async (IOrderRepository repository, TimeProvider clock, CancellationToken cancellationToken) =>
            TypedResults.Ok(ProductionScheduler.Plan(await repository.GetPlantStateAsync(cancellationToken), clock.GetUtcNow().UtcDateTime)))
            .WithTags("Scheduling").WithSummary("Next eligible action, compatible oven batches and current blocking reasons. Read-only calculation.");
        app.MapGet("/api/plc", async (IOrderRepository repository, CancellationToken cancellationToken) =>
        {
            var state = await repository.GetPlantStateAsync(cancellationToken);
            return TypedResults.Ok(new { Channel = "Persistent demo PLC", Settings = state.PlcSimulation,
                PlcCommand.TimeoutSeconds, PlcCommand.MaxAttempts, Commands = state.PlcCommands });
        }).WithTags("PLC").WithSummary("Durable transfer envelopes, acknowledgement state and retry history. Read-only; no live PLC protocol.");

        app.MapGet("/api/heats/{heatNumber}/genealogy", GetGenealogyAsync)
            .WithTags("Traceability")
            .WithName("GetHeatGenealogy")
            .WithSummary("Material batch genealogy: billet batches, loaded baskets and their transport history.");

        app.MapGet("/api/shift-summary", GetShiftSummaryAsync)
            .WithTags("Shift")
            .WithName("GetShiftSummary")
            .WithSummary("Key figures of the current shift (Early 06-14, Late 14-22, Night 22-06, plant time).");

        return app;
    }

    private static async Task<Ok<List<TransportDto>>> GetTransportsAsync(IOrderRepository repository, CancellationToken cancellationToken) =>
        TypedResults.Ok((await repository.GetTransportsAsync(cancellationToken)).Select(TransportDto.From).ToList());

    private static async Task<Ok<LoadPlanDto>> GetLoadPlanAsync(
        IOrderRepository repository, TimeProvider clock, CancellationToken cancellationToken)
    {
        var orders = await repository.GetAllAsync(cancellationToken);
        return TypedResults.Ok(LoadPlanDto.From(LoadPlanner.Plan(orders, Fleet.Extrusion, clock.GetUtcNow().UtcDateTime)));
    }

    private static async Task<Results<Ok<GenealogyDto>, NotFound>> GetGenealogyAsync(
        string heatNumber, IOrderRepository repository, CancellationToken cancellationToken)
    {
        var genealogy = HeatGenealogy.Build(heatNumber, await repository.GetAllAsync(cancellationToken));
        return genealogy.Roots.Count == 0 ? TypedResults.NotFound() : TypedResults.Ok(GenealogyDto.From(genealogy));
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
        var transports = await repository.GetTransportsAsync(cancellationToken);
        var summary = ShiftSummary.Calculate(orders, transports, clock.GetUtcNow().UtcDateTime, plant.Zone);
        return TypedResults.Ok(ShiftSummaryDto.From(summary, plant.Zone));
    }
}
