using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

/// <param name="Seeded">True when the 12 demo orders were inserted into an empty database.</param>
/// <param name="LegacyBackupPath">Where a database created by an older version (without migrations) was moved.</param>
public sealed record DatabaseInitializationResult(bool Seeded, string? LegacyBackupPath);

/// <summary>
/// Applies the EF Core migrations and seeds 12 demo orders only when the database has no order yet,
/// so restarts never duplicate or reset data.
/// </summary>
public sealed class DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory, TimeProvider timeProvider)
{
    public const string SeedUser = "demo data";

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var legacyBackup = await MoveLegacyDatabaseAsync(context.Database.GetDbConnection(), cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        if (await context.Orders.AnyAsync(cancellationToken))
        {
            return new DatabaseInitializationResult(false, legacyBackup);
        }

        context.Orders.AddRange(CreateDemoOrders(timeProvider.GetUtcNow().UtcDateTime));
        await context.SaveChangesAsync(cancellationToken);
        return new DatabaseInitializationResult(true, legacyBackup);
    }

    /// <summary>
    /// Version 1 created the schema with EnsureCreated, which migrations cannot upgrade.
    /// Such a file only holds demo data, so it is kept as a backup and a fresh database is created.
    /// </summary>
    private static async Task<string?> MoveLegacyDatabaseAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var path = connection.DataSource;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        await connection.OpenAsync(cancellationToken);
        bool isLegacy;
        try
        {
            isLegacy = await TableExistsAsync(connection, "TransportOrders", cancellationToken)
                && !await TableExistsAsync(connection, "__EFMigrationsHistory", cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }

        if (!isLegacy)
        {
            return null;
        }

        SqliteConnection.ClearAllPools();
        var backup = $"{path}.v1-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Move(path, backup);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                File.Move(path + suffix, backup + suffix);
            }
        }

        return backup;
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    /// <summary>
    /// 12 orders spread over the last five hours relative to <paramref name="utcNow"/>, so due times and
    /// overdue flags are meaningful on first launch. Billets and the profiles pressed from them share a heat number.
    /// </summary>
    internal static IReadOnlyList<TransportOrder> CreateDemoOrders(DateTime utcNow)
    {
        var start = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute / 5 * 5, 0, DateTimeKind.Utc)
            .AddHours(-5);

        (string Material, string Heat, int Quantity, string From, string To, OrderStatus Status, OrderPriority Priority, int DueMinutes)[] rows =
        [
            ("Billet 6060", "H-26031", 40, "Warehouse A", "Press 1", OrderStatus.Planned, OrderPriority.Normal, 360),
            ("Billet 6082", "H-26044", 32, "Warehouse A", "Press 2", OrderStatus.InTransit, OrderPriority.Urgent, 150),
            ("Profile 4711", "H-26031", 120, "Press 1", "Aging Oven", OrderStatus.Done, OrderPriority.Normal, 120),
            ("Profile 6082", "H-26044", 85, "Press 2", "Aging Oven", OrderStatus.Planned, OrderPriority.Normal, 420),
            ("Profile 4711", "H-26031", 120, "Aging Oven", "Packing", OrderStatus.InTransit, OrderPriority.Normal, 300),
            ("Billet 6060", "H-26052", 36, "Warehouse A", "Press 1", OrderStatus.Cancelled, OrderPriority.Normal, 240),
            ("Profile 6082", "H-26044", 85, "Aging Oven", "Packing", OrderStatus.Done, OrderPriority.Urgent, 90),
            ("Billet 6082", "H-26058", 28, "Warehouse A", "Press 2", OrderStatus.Planned, OrderPriority.Urgent, 150),
            ("Profile 4711", "H-26031", 96, "Packing", "Warehouse A", OrderStatus.InTransit, OrderPriority.Normal, 240),
            ("Profile 6082", "H-26052", 60, "Press 2", "Aging Oven", OrderStatus.Cancelled, OrderPriority.Normal, 200),
            ("Billet 6060", "H-26052", 44, "Warehouse A", "Press 1", OrderStatus.Done, OrderPriority.Normal, 120),
            ("Profile 4711", "H-26058", 150, "Press 1", "Aging Oven", OrderStatus.Planned, OrderPriority.Normal, 300),
        ];

        var orders = new List<TransportOrder>(rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var createdAt = start.AddMinutes(20 * i);
            var order = new TransportOrder($"TO-2026-{i + 1:D4}", row.Heat, row.Material, row.Quantity, row.From, row.To,
                row.Priority, createdAt.AddMinutes(row.DueMinutes), createdAt, SeedUser);

            // Reach the target status through the domain rules so stored data and history are consistent.
            switch (row.Status)
            {
                case OrderStatus.InTransit:
                    order.TransitionTo(OrderStatus.InTransit, SeedUser, createdAt.AddMinutes(20));
                    break;
                case OrderStatus.Done:
                    order.TransitionTo(OrderStatus.InTransit, SeedUser, createdAt.AddMinutes(20));
                    order.TransitionTo(OrderStatus.Done, SeedUser, createdAt.AddMinutes(55));
                    break;
                case OrderStatus.Cancelled:
                    order.TransitionTo(OrderStatus.Cancelled, SeedUser, createdAt.AddMinutes(10));
                    break;
            }

            orders.Add(order);
        }

        return orders;
    }
}
