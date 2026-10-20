using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Api;

public static class OperatorCommandEndpoints
{
    public static IEndpointRouteBuilder MapOperatorCommands(this IEndpointRouteBuilder app, string databasePath)
    {
        Map<QualityCommand, OperatorCommandResult>(app, "/api/packages/{id}/inspection", databasePath,
            (id, c, s, actor, ct) => s.InspectAsync(id, c, actor, ct), PlantPermissions.Quality);
        Map<BasketReloadCommand, OperatorCommandResult>(app, "/api/baskets/{id}/reload", databasePath,
            (id, c, s, actor, ct) => s.ReloadBasketAsync(id, c, actor, ct), PlantPermissions.Basket);
        Map<PlcSettingsCommand, OperatorCommandResult>(app, "/api/plc/simulation/settings", databasePath,
            (_, c, s, actor, ct) => s.SetPlcSettingsAsync(c, actor, ct), PlantPermissions.Plc);
        Map<PlcOperatorCommand, OperatorCommandResult>(app, "/api/plc/commands/{id}/retry", databasePath,
            (id, c, s, actor, ct) => s.RetryPlcAsync(CommandId(id), c, actor, ct), PlantPermissions.Plc);
        Map<PlcOperatorCommand, OperatorCommandResult>(app, "/api/plc/commands/{id}/manual-arrival", databasePath,
            (id, c, s, actor, ct) => s.ConfirmPlcAsync(CommandId(id), c, actor, ct), PlantPermissions.Plc);
        Map<OrderStateCommand, OperatorCommandResult>(app, "/api/orders/{id}/state", databasePath,
            (id, c, s, actor, ct) => s.ChangeOrderAsync(id, c, actor, ct), "OrderState");
        Map<DispatchCommand, OperatorCommandResult>(app, "/api/transports/dispatch", databasePath,
            (_, c, s, actor, ct) => s.DispatchAsync(c, actor, ct), PlantPermissions.Dispatch);
        Map<TransportArrivalCommand, OperatorCommandResult>(app, "/api/transports/{id}/manual-arrival", databasePath,
            (id, c, s, actor, ct) => s.ConfirmTransportAsync(id, c, actor, ct), PlantPermissions.Arrival);
        Map<DemoStepCommand, SimulationResult>(app, "/api/simulation/step", databasePath,
            (_, _, s, _, ct) => s.StepAsync(ct), PlantPermissions.Simulation, preventRuntimeStep: true);
        return app;
    }
    private static Guid CommandId(string id) => Guid.TryParse(id, out var parsed) && parsed != Guid.Empty
        ? parsed : throw new ArgumentException("Invalid command id.");
    private static void Map<T, R>(IEndpointRouteBuilder app, string route, string databasePath,
        Func<string, T, PlantCommandService, string, CancellationToken, Task<R>> action, string permission, bool preventRuntimeStep = false)
        where T : IPlantCommandRequest
    {
        // Resolve the optional route identifier directly; no route parameter is required for singleton commands.
        app.MapPost(route, async (T command, HttpContext http, PlantCommandService service,
            PlantRuntimeStatus runtime, OperatorAccessPolicy access, ILoggerFactory logging, PlantServiceIdentity identity, CancellationToken ct) =>
        {
            if (command is OrderStateCommand order && !access.Allows(http.User,
                order.TargetStatus == OrderStatus.Cancelled ? PlantPermissions.OrderCancel : PlantPermissions.OrderQuality))
            {
                logging.CreateLogger("OperatorAccess").LogWarning("Order action denied: {User} {Status} {Path}",
                    http.User.Identity?.Name, order.TargetStatus, http.Request.Path);
                return Results.Forbid();
            }
            if (string.IsNullOrWhiteSpace(command.ExpectedDatabasePath) && string.IsNullOrWhiteSpace(command.ExpectedPlantId))
                return Results.Problem(statusCode: 400, title: "Database path required");
            if (!identity.Matches(command.ExpectedPlantId, command.ExpectedDatabasePath))
                return Results.Problem(statusCode: 409, title: "Different database");
            try
            {
                if (preventRuntimeStep && runtime.Running) return Results.Ok(new SimulationResult(SimulationAction.CentralService));
                return Results.Ok(await action(http.Request.RouteValues["id"]?.ToString() ?? "", command, service, http.User.Identity!.Name!, ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.Problem(statusCode: 400, title: "Invalid command", detail: ex.Message); }
            catch (Exception ex) when (ex is TransportRuleException or InvalidStatusTransitionException or DbUpdateConcurrencyException)
            { return Results.Problem(statusCode: 409, title: "Plant state changed or action blocked", detail: ex.Message); }
        }).RequireAuthorization(permission == "OrderState" ? new Microsoft.AspNetCore.Authorization.AuthorizeAttribute()
            : new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { Policy = permission }).WithTags("Operator commands");
    }
}
