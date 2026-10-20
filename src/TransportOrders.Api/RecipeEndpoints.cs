using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using TransportOrders.Core;
namespace TransportOrders.Api;

public sealed record RecipeCommand(Guid ExpectedVersion, RecipeParameters? Parameters, string Reason, string ExpectedPlantId);
public static class RecipeEndpoints
{
    public static void MapRecipeEndpoints(this WebApplication app, OperatorAccessPolicy access)
    {
        app.MapGet("/api/recipes", async (IOrderRepository repository, CancellationToken ct) =>
            (await repository.GetPlantStateAsync(ct)).Recipes.Select(OvenRecipeSnapshot.From));
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .RequireAssertion(c => access.Describe(c.User, "").Roles.Contains("Supervisor")).Build();
        foreach (var action in new[] { "create", "revise", "validate", "archive" })
        {
            var operation = action;
            app.MapPost($"/api/recipes/{{id}}/{operation}", async (string id, RecipeCommand command,
                HttpContext http, IRecipeRepository recipes, PlantServiceIdentity identity, PlantOperationGate gate,
                TimeProvider clock, CancellationToken ct) =>
            {
                if (command.ExpectedPlantId != identity.Id) return Results.Conflict();
                try
                {
                    await gate.RunAsync(async () =>
                    {
                        if (operation == "create") await recipes.CreateAsync(id, command.Parameters ?? throw new ArgumentException("Parameters required."),
                            http.User.Identity!.Name!, command.Reason, clock.GetUtcNow().UtcDateTime, ct);
                        else await recipes.ChangeAsync(id, command.ExpectedVersion, operation, command.Parameters,
                            http.User.Identity!.Name!, command.Reason, clock.GetUtcNow().UtcDateTime, ct);
                        return true;
                    }, ct);
                    return Results.Ok();
                }
                catch (KeyNotFoundException) { return Results.NotFound(); }
                catch (ArgumentException) { return Results.BadRequest(); }
                catch (Exception ex) when (ex is TransportRuleException or DbUpdateConcurrencyException) { return Results.Conflict(); }
            }).RequireAuthorization(policy);
        }
    }
}
