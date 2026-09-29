using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransportOrders.Core;

namespace TransportOrders.Data;

/// <param name="Seeded">True when the demo data was inserted into an empty database.</param>
/// <param name="LegacyBackupPath">Where a database created by version 1 (without migrations) was moved.</param>
public sealed record DatabaseInitializationResult(bool Seeded, string? LegacyBackupPath);

/// <summary>
/// Applies the EF Core migrations and seeds the demo data (12 orders, 2 transports) only when the database
/// has no order yet, so restarts never duplicate or reset data.
/// </summary>
public sealed class DatabaseInitializer(IDbContextFactory<AppDbContext> contextFactory, TimeProvider timeProvider)
{
    // Demo users are the plant's departments; the demo is presented to German-speaking plants.
    public const string PlanningUser = "Planung";
    public const string DispatchUser = "Disposition";
    public const string QualityUser = "Qualitätssicherung";

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var legacyBackup = await MoveLegacyDatabaseAsync(context.Database.GetDbConnection(), cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        if (await context.Orders.AnyAsync(cancellationToken))
        {
            return new DatabaseInitializationResult(false, legacyBackup);
        }

        var (orders, transports) = CreateDemoData(timeProvider.GetUtcNow().UtcDateTime);
        context.Transports.AddRange(transports);
        context.Orders.AddRange(orders);
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
    /// A special-steel plant over the last five hours relative to <paramref name="utcNow"/>: ingots from the melt shop
    /// go to ESR remelting, ESR ingots to the forge, forged bars to heat treatment and ultrasonic testing.
    /// Pieces carry their heat number and the piece they were made from. Every status is reached through the
    /// domain rules, so history and transports are consistent. The open orders are chosen so the load planner
    /// has full loads to propose on two routes and an urgent part load on a third.
    /// </summary>
    internal static (IReadOnlyList<TransportOrder> Orders, IReadOnlyList<Transport> Transports) CreateDemoData(DateTime utcNow)
    {
        var start = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute / 5 * 5, 0, DateTimeKind.Utc)
            .AddHours(-5);
        DateTime At(int minutes) => start.AddMinutes(minutes);

        // Plant data in German, like the department names above: stations, materials and notes.
        const string melt = "Stahlwerk", esr = "ESU-Anlage", forge = "Schmiede", heat = "Wärmebehandlung", ut = "US-Prüfung";
        const OrderPriority normal = OrderPriority.Normal, urgent = OrderPriority.Urgent;

        (string Heat, string Piece, string? Parent, string Material, int Pieces, decimal Tonnes, decimal Metres,
            string From, string To, OrderPriority Priority, int Created, int DueAfter)[] rows =
        [
            ("H-26031", "H-26031-1", null, "1.2379 Rohblock", 1, 19.6m, 2.9m, melt, esr, normal, 0, 240),
            ("H-26031", "H-26031-2", null, "1.2379 Rohblock", 1, 17.8m, 2.7m, melt, esr, normal, 0, 240),
            ("H-26031", "H-26031-1A", "H-26031-1", "1.2379 Schmiedestab", 4, 12.4m, 4.2m, forge, heat, normal, 120, 300),
            ("H-26031", "H-26031-1B", "H-26031-1", "1.2379 Schmiedestab", 3, 9.8m, 3.6m, forge, heat, normal, 130, 320),
            ("H-26031", "H-26031-2A", "H-26031-2", "1.2379 Schmiedestab", 3, 8.6m, 2.6m, forge, heat, urgent, 140, 200),
            ("H-26031", "H-26031-2B", "H-26031-2", "1.2379 Schmiedestab", 2, 6.3m, 1.4m, forge, heat, normal, 150, 330),
            ("H-26044", "H-26044-1", null, "1.3343 ESU-Block", 1, 18.4m, 2.8m, esr, forge, urgent, 230, 60),
            ("H-26044", "H-26044-2", null, "1.3343 ESU-Block", 1, 18.1m, 2.8m, esr, forge, urgent, 60, 220),
            ("H-26052", "H-26052-1A", "H-26052-1", "1.2344 Schmiedeblock", 2, 3.1m, 1.9m, heat, ut, normal, 160, 200),
            ("H-26052", "H-26052-1B", "H-26052-1", "1.2344 Schmiedeblock", 2, 4.2m, 1.8m, heat, ut, normal, 170, 220),
            ("H-26052", "H-26052-1C", "H-26052-1", "1.2344 Schmiedeblock", 1, 2.4m, 1.2m, heat, ut, normal, 180, 200),
            ("H-26058", "H-26058-1", null, "1.2344 Rohblock", 1, 16.0m, 2.6m, melt, esr, normal, 0, 240),
        ];

        var orders = rows.Select((r, i) => new TransportOrder(
            $"TO-2026-{i + 1:D4}", r.Heat, r.Piece, r.Parent, r.Material, r.Pieces, r.Tonnes, r.Metres,
            r.From, r.To, r.Priority, At(r.Created + r.DueAfter), At(r.Created), PlanningUser)).ToList();
        TransportOrder Order(int number) => orders[number - 1];

        var carrier = Fleet.Default.Single(v => v.Type == VehicleType.HeavyLoadCarrier);
        var year = utcNow.Year;

        // Two short, heavy ingots: the payload is used up long before the loading length.
        var first = Transport.Dispatch($"TR-{year}-0001", carrier, [Order(1), Order(2)], DispatchUser, At(40), PartLoadReason.PayloadLimit);
        first.Complete([Order(1), Order(2)], DispatchUser, At(85));

        // The forge was waiting for this ingot, so it left alone.
        var second = Transport.Dispatch($"TR-{year}-0002", carrier, [Order(8)], DispatchUser, At(200), PartLoadReason.UrgentMaterial);

        Order(12).TransitionTo(OrderStatus.Cancelled, PlanningUser, At(30), "Chemische Analyse außerhalb der Spezifikation");
        Order(11).TransitionTo(OrderStatus.OnHold, QualityUser, At(210), "Härte unter Spezifikation, Nachprüfung");

        return (orders, [first, second]);
    }
}
