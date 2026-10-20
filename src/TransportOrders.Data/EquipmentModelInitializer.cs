using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

internal static class EquipmentModelInitializer
{
    internal static async Task InitializeAsync(AppDbContext context, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.Equipment.AnyAsync(cancellationToken)) return;
        foreach (var station in await context.Locations.ToListAsync(cancellationToken))
        {
            EquipmentKind? kind = station.Kind switch
            {
                StationKind.Press => EquipmentKind.Press, StationKind.AgingOven => EquipmentKind.Oven,
                StationKind.Packing => EquipmentKind.Packing, _ => null,
            };
            if (kind.HasValue) context.Equipment.Add(new(station.Id, kind.Value, station.Id, now));
        }
        var shuttle = new PlantEquipment(PlantEquipment.SharedShuttle, EquipmentKind.Shuttle, null, now);
        var active = await context.Transports.Where(t => t.Status == TransportStatus.InTransit).ToListAsync(cancellationToken);
        if (active.Count > 1) throw new TransportRuleException("Multiple active legacy transfers require review before shared equipment can be initialized.");
        if (active.Count == 1) shuttle.Start(active[0].TransportNumber, "INITIALIZER", now);
        context.Equipment.Add(shuttle);
        // Accelerated demo recipes. The parameters are deliberately illustrative.
        context.Recipes.AddRange(new OvenRecipe("DEMO-6060", "6060", 180m, 2, 3, 2),
            new OvenRecipe("DEMO-6063", "6063", 180m, 2, 3, 2), new OvenRecipe("DEMO-6082", "6082", 190m, 2, 4, 2));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
