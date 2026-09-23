using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

/// <summary>
/// Creates the SQLite schema (EnsureCreated, no migrations) and seeds 12 demo orders
/// only when the table is still empty, so restarts never duplicate or reset data.
/// </summary>
public sealed class DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        if (await context.Orders.AnyAsync(cancellationToken))
        {
            return;
        }

        context.Orders.AddRange(CreateDemoOrders());
        await context.SaveChangesAsync(cancellationToken);
    }

    internal static IReadOnlyList<TransportOrder> CreateDemoOrders()
    {
        var day = new DateTime(2026, 3, 2, 6, 0, 0, DateTimeKind.Utc);

        (string Material, int Quantity, string From, string To, OrderStatus Status)[] rows =
        [
            ("Billet 6060", 40, "Warehouse A", "Press 1", OrderStatus.Planned),
            ("Billet 6082", 32, "Warehouse A", "Press 2", OrderStatus.InTransit),
            ("Profile 4711", 120, "Press 1", "Aging Oven", OrderStatus.Done),
            ("Profile 6082", 85, "Press 2", "Aging Oven", OrderStatus.Planned),
            ("Profile 4711", 120, "Aging Oven", "Packing", OrderStatus.InTransit),
            ("Billet 6060", 36, "Warehouse A", "Press 1", OrderStatus.Cancelled),
            ("Profile 6082", 85, "Aging Oven", "Packing", OrderStatus.Done),
            ("Billet 6082", 28, "Warehouse A", "Press 2", OrderStatus.Planned),
            ("Profile 4711", 96, "Packing", "Warehouse A", OrderStatus.InTransit),
            ("Profile 6082", 60, "Press 2", "Aging Oven", OrderStatus.Cancelled),
            ("Billet 6060", 44, "Warehouse A", "Press 1", OrderStatus.Done),
            ("Profile 4711", 150, "Press 1", "Aging Oven", OrderStatus.Planned),
        ];

        var orders = new List<TransportOrder>(rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            var (material, quantity, from, to, status) = rows[i];
            var createdAt = day.AddMinutes(45 * i);
            var order = new TransportOrder($"TO-2026-{i + 1:D4}", material, quantity, from, to, createdAt);

            // Reach the target status through the domain rules so stored data is always consistent.
            switch (status)
            {
                case OrderStatus.InTransit:
                    order.TransitionTo(OrderStatus.InTransit, createdAt.AddMinutes(20));
                    break;
                case OrderStatus.Done:
                    order.TransitionTo(OrderStatus.InTransit, createdAt.AddMinutes(20));
                    order.TransitionTo(OrderStatus.Done, createdAt.AddMinutes(55));
                    break;
                case OrderStatus.Cancelled:
                    order.TransitionTo(OrderStatus.Cancelled, createdAt.AddMinutes(10));
                    break;
            }

            orders.Add(order);
        }

        return orders;
    }
}
