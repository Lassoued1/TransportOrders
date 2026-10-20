using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;
namespace TransportOrders.Data;
public sealed class EfRecipeRepository(IDbContextFactory<AppDbContext> factory) : IRecipeRepository
{
    public async Task CreateAsync(string id, RecipeParameters p, string actor, string reason, DateTime now, CancellationToken ct = default)
    {
        var recipe = OvenRecipe.CreateDraft(id, p, actor, reason, now);
        await using var context = await factory.CreateDbContextAsync(ct);
        if (await context.Recipes.AnyAsync(r => r.Id.ToUpper() == id.ToUpper(), ct)) throw new TransportRuleException("Recipe identifier already exists.");
        context.Recipes.Add(recipe); await context.SaveChangesAsync(ct);
    }
    public async Task ChangeAsync(string id, Guid version, string action, RecipeParameters? p, string actor, string reason, DateTime now, CancellationToken ct = default)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        var recipe = await context.Recipes.Include(r => r.History).SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new KeyNotFoundException();
        switch (action)
        {
            case "revise": recipe.Revise(version, p ?? throw new ArgumentException("Parameters required."), actor, reason, now); break;
            case "validate": recipe.Validate(version, actor, reason, now); break;
            case "archive": recipe.Archive(version, actor, reason, now); break;
            default: throw new ArgumentException("Unknown recipe action.");
        }
        await context.SaveChangesAsync(ct);
    }
}
