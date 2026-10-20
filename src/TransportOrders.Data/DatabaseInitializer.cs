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

    private static async Task ImportRecipeHistoryAsync(AppDbContext context, DateTime now, CancellationToken ct)
    {
        var recipes = await context.Recipes.Where(r => r.Version == Guid.Empty).ToListAsync(ct);
        foreach (var recipe in recipes) recipe.ImportLegacy(now);
        if (recipes.Count > 0) await context.SaveChangesAsync(ct);
    }

    public async Task<DatabaseInitializationResult> InitializeSchemaOnlyAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        await ImportRecipeHistoryAsync(context, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        return new(false, null);
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var legacyBackup = await MoveLegacyDatabaseAsync(context.Database.GetDbConnection(), cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        await ImportRecipeHistoryAsync(context, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        if (await context.Orders.AnyAsync(cancellationToken))
        {
            await PlantModelInitializer.InitializeAsync(context, cancellationToken);
            await EquipmentModelInitializer.InitializeAsync(context, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            await PlcModelInitializer.InitializeAsync(context, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            return new DatabaseInitializationResult(false, legacyBackup);
        }

        var (orders, transports) = CreateDemoData(timeProvider.GetUtcNow().UtcDateTime);
        context.Transports.AddRange(transports);
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync(cancellationToken);
        await PlantModelInitializer.InitializeAsync(context, cancellationToken);
        await EquipmentModelInitializer.InitializeAsync(context, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        await PlcModelInitializer.InitializeAsync(context, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
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
    /// Extrusion baskets moving from presses through aging ovens and packing to shipping.
    /// PieceNumber is the stable loaded-basket identifier; HeatNumber is the material batch.
    /// Every initial status is reached through the audited domain methods.
    /// </summary>
    internal static (IReadOnlyList<TransportOrder> Orders, IReadOnlyList<Transport> Transports) CreateDemoData(DateTime utcNow)
    {
        var start = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute / 5 * 5, 0, DateTimeKind.Utc)
            .AddHours(-5);
        DateTime At(int minutes) => start.AddMinutes(minutes);

        const string press1 = "PRESS-01", press2 = "PRESS-02", oven1 = "OVEN-01", oven2 = "OVEN-02", pack1 = "PACK-01", pack2 = "PACK-02", ship = "SHIP-01";
        const OrderPriority normal = OrderPriority.Normal, urgent = OrderPriority.Urgent;

        (string Heat, string Piece, string? Parent, string Material, int Pieces, decimal Tonnes, decimal Metres,
            string From, string To, OrderPriority Priority, int Created, int DueAfter)[] rows =
        [
            ("AL-26031", "AL-26031-K01", "AL-26031-B01", "EN AW-6060 profiles", 48, 1.2m, 6.4m, press1, oven1, normal, 0, 240),
            ("AL-26031", "AL-26031-K02", "AL-26031-B01", "EN AW-6060 profiles", 48, 1.4m, 6.4m, press1, oven1, normal, 0, 240),
            ("AL-26031", "AL-26031-K03", "AL-26031-B01", "EN AW-6060 profiles", 36, 1.1m, 6.4m, press1, oven2, normal, 120, 300),
            ("AL-26031", "AL-26031-K04", "AL-26031-B01", "EN AW-6060 profiles", 36, 1.2m, 6.4m, press1, oven2, normal, 130, 320),
            ("AL-26031", "AL-26031-K05", "AL-26031-B02", "EN AW-6060 profiles", 24, 0.9m, 6.4m, press1, oven2, urgent, 140, 200),
            ("AL-26031", "AL-26031-K06", "AL-26031-B02", "EN AW-6060 profiles", 24, 0.8m, 6.4m, press1, oven2, normal, 150, 330),
            ("AL-26044", "AL-26044-K01", "AL-26044-B01", "EN AW-6082 profiles", 20, 1.8m, 6.4m, press2, oven2, urgent, 230, 60),
            ("AL-26044", "AL-26044-K02", "AL-26044-B01", "EN AW-6082 profiles", 20, 1.6m, 6.4m, oven2, pack1, urgent, 60, 220),
            ("AL-26052", "AL-26052-K01", "AL-26052-B01", "EN AW-6063 profiles", 60, 1.3m, 6.4m, pack2, ship, normal, 160, 200),
            ("AL-26052", "AL-26052-K02", "AL-26052-B01", "EN AW-6063 profiles", 60, 1.4m, 6.4m, press2, oven1, normal, 170, 220),
            ("AL-26052", "AL-26052-K03", "AL-26052-B01", "EN AW-6063 profiles", 30, 0.7m, 6.4m, press2, oven1, normal, 180, 200),
            ("AL-26058", "AL-26058-K01", "AL-26058-B01", "EN AW-6060 profiles", 24, 0.8m, 6.4m, press2, oven1, normal, 0, 240),
        ];

        var orders = rows.Select((r, i) => new TransportOrder(
            $"TO-2026-{i + 1:D4}", r.Heat, r.Piece, r.Parent, r.Material, r.Pieces, r.Tonnes, r.Metres,
            r.From, r.To, r.Priority, At(r.Created + r.DueAfter), At(r.Created), PlanningUser)).ToList();
        TransportOrder Order(int number) => orders[number - 1];

        var carrier = Fleet.Extrusion.Single(v => v.Type == VehicleType.OverheadCrane);
        var year = utcNow.Year;

        // Two loaded baskets already delivered to the first aging oven.
        var first = Transport.Dispatch($"TR-{year}-0001", carrier, [Order(1), Order(2)], DispatchUser, At(40));
        first.Complete([Order(1), Order(2)], DispatchUser, At(85));

        // One aged basket is currently on the shuttle to packing.
        var second = Transport.Dispatch($"TR-{year}-0002", ExtrusionSimulator.Shuttle, [Order(8)], DispatchUser, At(200));

        Order(12).TransitionTo(OrderStatus.Cancelled, PlanningUser, At(30), "Auftrag storniert; Korb bleibt am Pressenauslauf");
        Order(11).TransitionTo(OrderStatus.OnHold, QualityUser, At(210), "Oberflächenprüfung ausstehend");

        return (orders, [first, second]);
    }
}
