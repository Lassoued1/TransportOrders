using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;

namespace TransportOrders.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddTransportOrdersData(this IServiceCollection services, string databasePath)
    {
        // A 5 s busy timeout (instead of 30 s) reports a locked database quickly instead of leaving the user waiting.
        services.AddDbContextFactory<AppDbContext>(
            options => options.UseSqlite($"Data Source={databasePath};Default Timeout=5"));
        services.AddSingleton<IOrderRepository, EfOrderRepository>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}
