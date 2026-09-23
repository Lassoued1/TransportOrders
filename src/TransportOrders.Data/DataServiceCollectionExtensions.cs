using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;

namespace TransportOrders.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddTransportOrdersData(this IServiceCollection services, string databasePath)
    {
        services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddSingleton<IOrderRepository, EfOrderRepository>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}
