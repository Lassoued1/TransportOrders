using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Api;

public sealed record EquipmentControlRequest(Guid ExpectedVersion, EquipmentMode Mode, EquipmentCondition Condition,
    string? Reason, string? ExpectedDatabasePath, string? ExpectedPlantId = null);
public sealed record EquipmentControlResponse(string EquipmentId, EquipmentMode Mode, EquipmentCondition Condition,
    string Operator, DateTime AppliedAtUtc);

public static class EquipmentControlEndpoints
{
    public static IEndpointRouteBuilder MapEquipmentControlEndpoints(this IEndpointRouteBuilder app, string databasePath)
    {
        app.MapPost("/api/equipment/{id}/control", async (string id, EquipmentControlRequest request,
            HttpContext http, IOrderRepository repository, TimeProvider clock, PlantOperationGate operations,
            PlantServiceIdentity identity, CancellationToken cancellationToken) =>
        {
            if (request.ExpectedVersion == Guid.Empty || !Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.Condition)
                || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000
                || string.IsNullOrWhiteSpace(request.ExpectedDatabasePath) && string.IsNullOrWhiteSpace(request.ExpectedPlantId))
                return Results.Problem(statusCode: 400, title: "Invalid equipment control",
                    detail: "A version, valid mode/condition, reason (1–1000 characters) and database path are required.");
            // A station configured for another plant must not silently operate this service's equipment.
            if (!identity.Matches(request.ExpectedPlantId, request.ExpectedDatabasePath))
                return Results.Problem(statusCode: 409, title: "Different database", detail: "The station and service databases differ.");
            var state = await repository.GetPlantStateAsync(cancellationToken);
            if (!state.Equipment.Any(e => e.Id == id)) return Results.NotFound();
            var actor = http.User.Identity!.Name!;
            var now = clock.GetUtcNow().UtcDateTime;
            try
            {
                await operations.RunAsync(async () =>
                {
                    now = clock.GetUtcNow().UtcDateTime;
                    await repository.SetEquipmentControlAsync(id, request.ExpectedVersion, request.Mode, request.Condition,
                        actor, request.Reason.Trim(), now, cancellationToken);
                    return 0;
                }, cancellationToken);
                return Results.Ok(new EquipmentControlResponse(id, request.Mode, request.Condition, actor, now));
            }
            catch (Exception ex) when (ex is TransportRuleException or DbUpdateConcurrencyException)
            {
                return Results.Problem(statusCode: 409, title: "Equipment changed",
                    detail: "Reload the equipment state before submitting another control command.");
            }
        }).RequireAuthorization(PlantPermissions.Equipment).WithTags("Equipment")
            .WithSummary("Change equipment controls with authenticated operator identity and an expected version.");
        return app;
    }
}
