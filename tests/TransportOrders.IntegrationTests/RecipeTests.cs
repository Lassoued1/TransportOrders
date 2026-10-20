using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Api;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public class RecipeTests
{
    private static readonly RecipeParameters Parameters = new("6060", 185.5m, 2, 4, 3);
    [Fact]
    public async Task Draft_validation_revision_and_archive_survive_restart_without_changing_active_cycles()
    {
        using var db = new TempDatabase();
        using var services = new ServiceCollection().AddTransportOrdersData(db.Path).BuildServiceProvider();
        var init = services.GetRequiredService<DatabaseInitializer>(); await init.InitializeAsync();
        var plant = services.GetRequiredService<IOrderRepository>();
        var recipes = services.GetRequiredService<IRecipeRepository>();
        await recipes.CreateAsync("TEST-6060", Parameters, "supervisor", "Simulation test", DateTime.UtcNow);
        var state = await plant.GetPlantStateAsync();
        var draft = state.Recipes.Single(r => r.Id == "TEST-6060");
        Assert.Equal(RecipeStatus.Draft, draft.Status);
        Assert.False(draft.Supports(state.MaterialLots.Single(l => l.Number == state.Baskets.First(b => b.LocationId == "OVEN-01").MaterialLotNumber)));
        var baskets = state.Baskets.Where(b => b.LocationId == "OVEN-01").Select(b => b.Number).ToArray();
        await Assert.ThrowsAsync<TransportRuleException>(() => plant.StartOvenCycleAsync("OVEN-01", draft.Id, baskets, "op", DateTime.UtcNow));
        await recipes.ChangeAsync(draft.Id, draft.Version, "validate", null, "supervisor", "Reviewed", DateTime.UtcNow);
        state = await plant.GetPlantStateAsync(); var validated = state.Recipes.Single(r => r.Id == draft.Id);
        Assert.Equal(RecipeStatus.Validated, validated.Status);
        await plant.StartOvenCycleAsync("OVEN-01", draft.Id, baskets, "op", DateTime.UtcNow);
        await recipes.ChangeAsync(draft.Id, validated.Version, "revise", Parameters with { TargetTemperature = 200, HoldingMinutes = 8 }, "supervisor", "New simulation", DateTime.UtcNow);
        await Assert.ThrowsAsync<TransportRuleException>(() => recipes.ChangeAsync(draft.Id, validated.Version, "archive", null, "supervisor", "Stale", DateTime.UtcNow));
        await init.InitializeAsync(); state = await plant.GetPlantStateAsync();
        var revised = state.Recipes.Single(r => r.Id == draft.Id);
        Assert.Equal(RecipeStatus.Draft, revised.Status); Assert.Equal(2, revised.Revision);
        var cycle = Assert.Single(state.Cycles);
        Assert.Equal(1, cycle.RecipeRevision); Assert.Equal(185.5m, cycle.TargetTemperature); Assert.Equal(4, cycle.HoldingMinutes);
        await recipes.ChangeAsync(draft.Id, revised.Version, "archive", null, "supervisor", "Retired", DateTime.UtcNow);
        await init.InitializeAsync(); state = await plant.GetPlantStateAsync();
        var archived = state.Recipes.Single(r => r.Id == draft.Id);
        Assert.Equal(RecipeStatus.Archived, archived.Status); Assert.Equal(4, archived.History.Count);
        Assert.Equal(185.5m, archived.History.First().TargetTemperature);
        Assert.All(archived.History, e => Assert.Equal("supervisor", e.Actor));
        await Assert.ThrowsAsync<TransportRuleException>(() => recipes.ChangeAsync(draft.Id, archived.Version, "revise", Parameters, "supervisor", "No", DateTime.UtcNow));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        var encoded = JsonSerializer.Serialize(PlantStateSnapshot.From(state), options);
        Assert.Equal(encoded, JsonSerializer.Serialize(PlantStateSnapshot.From(JsonSerializer.Deserialize<PlantStateSnapshot>(encoded, options)!.Restore()), options));
    }

    [Fact]
    public async Task Migration_retains_existing_parameters_and_imports_history_only_once()
    {
        using var db = new TempDatabase();
        using var services = new ServiceCollection().AddTransportOrdersData(db.Path).BuildServiceProvider();
        await using (var context = await services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261012160129_PersistedAlarmHistory");
            await context.Database.ExecuteSqlRawAsync("INSERT INTO OvenRecipes (Id,Alloy,TargetTemperature,HeatingMinutes,HoldingMinutes,CoolingMinutes) VALUES ('LEGACY','6060','181.5',2,3,2)");
        }
        var init = services.GetRequiredService<DatabaseInitializer>(); await init.InitializeSchemaOnlyAsync(); await init.InitializeSchemaOnlyAsync();
        var recipe = Assert.Single((await services.GetRequiredService<IOrderRepository>().GetPlantStateAsync()).Recipes);
        Assert.Equal(181.5m, recipe.TargetTemperature); Assert.Equal(RecipeStatus.Validated, recipe.Status);
        Assert.Equal(1, recipe.Revision); Assert.NotEqual(Guid.Empty, recipe.Version);
        Assert.Equal("Imported", Assert.Single(recipe.History).Action);
    }

    [Theory]
    [InlineData("Quality")][InlineData("Maintenance")][InlineData("Logistics")][InlineData("Unmapped")]
    public async Task Recipe_management_requires_supervisor(string role)
    {
        using var factory = new RoleApiFactory(); using var client = factory.CreateClient();
        var id = factory.Services.GetRequiredService<PlantServiceIdentity>().Id;
        var command = new RecipeCommand(Guid.Empty, Parameters, "Test", id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/recipes/NEW/create", command)).StatusCode);
        client.DefaultRequestHeaders.Add("Test-User", $"PLANT\\{role}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/recipes/NEW/create", command)).StatusCode);
    }

    [Fact]
    public async Task Api_checks_site_input_duplicates_and_stale_versions_and_records_windows_actor()
    {
        using var factory = new RoleApiFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-User", "PLANT\\Supervisor");
        var id = factory.Services.GetRequiredService<PlantServiceIdentity>().Id;
        var command = new RecipeCommand(Guid.Empty, Parameters, "Create simulation", id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/recipes/NEW/create", command with { ExpectedPlantId = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recipes/NEW/create", command with { Reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/recipes/NEW/create", command with { Parameters = Parameters with { HeatingMinutes = int.MaxValue } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/recipes/NEW/create", command)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/recipes/new/create", command)).StatusCode);
        var state = await factory.Services.GetRequiredService<IOrderRepository>().GetPlantStateAsync(); var recipe = state.Recipes.Single(r => r.Id == "NEW");
        command = command with { ExpectedVersion = recipe.Version, Parameters = null };
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/recipes/MISSING/validate", command)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/recipes/NEW/validate", command)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/recipes/NEW/archive", command)).StatusCode);
        recipe = (await factory.Services.GetRequiredService<IOrderRepository>().GetPlantStateAsync()).Recipes.Single(r => r.Id == "NEW");
        Assert.Equal("PLANT\\Supervisor", recipe.History.Last().Actor);
        Assert.Equal(RecipeStatus.Validated, recipe.Status);
    }
}
