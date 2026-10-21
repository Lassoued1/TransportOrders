using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Api;

public static class RuntimeControlEndpoints
{
    public static void MapRuntimeControls(this IEndpointRouteBuilder app, bool enabled)
    {
        app.MapGet("/api/runtime/history", (IRuntimeControlRepository controls, CancellationToken ct) => controls.HistoryAsync(ct));
        app.MapPost("/api/runtime/control", async (RuntimeControlCommand command, HttpContext http,
            IRuntimeControlRepository controls, PlantOperationGate gate, PlantRuntimeStatus runtime,
            PlantServiceIdentity identity, TimeProvider clock, CancellationToken ct) =>
        {
            if (command.ExpectedVersion == Guid.Empty || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000
                || (string.IsNullOrWhiteSpace(command.ExpectedPlantId) && string.IsNullOrWhiteSpace(command.ExpectedDatabasePath)))
                return Results.BadRequest();
            if (!identity.Matches(command.ExpectedPlantId, command.ExpectedDatabasePath)) return Results.Conflict();
            if (!enabled || !runtime.Running) return Results.Conflict();
            try
            {
                return Results.Ok(await gate.RunAsync(() => controls.ChangeAsync(command,
                    http.User.Identity!.Name!, clock.GetUtcNow().UtcDateTime, ct), ct));
            }
            catch (ArgumentException) { return Results.BadRequest(); }
            catch (Exception ex) when (ex is TransportRuleException or DbUpdateConcurrencyException)
            { return Results.Conflict(); }
        }).RequireAuthorization(PlantPermissions.Runtime);
    }
}
