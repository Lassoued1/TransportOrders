using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransportOrders.Core;
using TransportOrders.Data;

namespace TransportOrders.IntegrationTests;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly TempDatabase _database = new();
    private readonly ServiceProvider _services;
    private string DirectoryPath => Path.GetDirectoryName(_database.Path)!;
    private IOrderRepository Repository => _services.GetRequiredService<IOrderRepository>();
    public DatabaseBackupTests() => _services = new ServiceCollection().AddTransportOrdersData(_database.Path).BuildServiceProvider();
    private async Task Initialize() => await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    [Fact]
    public async Task Online_backup_includes_committed_WAL_state_without_uncommitted_changes()
    {
        await Initialize();
        using var writer = new SqliteConnection($"Data Source={_database.Path};Pooling=False");
        writer.Open();
        using var command = writer.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;";
        command.ExecuteNonQuery();
        var equipment = (await Repository.GetPlantStateAsync()).Equipment.First();
        await Repository.SetEquipmentControlAsync(equipment.Id, equipment.Version, EquipmentMode.Manual,
            EquipmentCondition.Operational, "test", "Committed before backup", DateTime.UtcNow);
        var expected = JsonSerializer.Serialize(PlantStateSnapshot.From(await Repository.GetPlantStateAsync()));
        using var pending = writer.BeginTransaction();
        command.Transaction = pending;
        command.CommandText = "UPDATE PlantEquipment SET Mode=0";
        command.ExecuteNonQuery();
        var result = DatabaseBackup.Create(_database.Path, DirectoryPath, "plant-one");
        pending.Rollback();
        Assert.False(File.Exists(result.DatabasePath + "-wal"));
        Assert.False(File.Exists(result.DatabasePath + "-shm"));
        DatabaseBackup.Verify(result.DatabasePath, "plant-one");
        var restored = Path.Combine(DirectoryPath, "restored.db");
        DatabaseBackup.RestoreCopy(result.DatabasePath, restored, "plant-one");
        using var services = new ServiceCollection().AddTransportOrdersData(restored).BuildServiceProvider();
        var actual = await services.GetRequiredService<IOrderRepository>().GetPlantStateAsync();
        Assert.Equal(expected, JsonSerializer.Serialize(PlantStateSnapshot.From(actual)));
    }
    [Fact]
    public async Task Restored_PLC_execution_receives_late_reply_without_resend_or_duplicate_arrival()
    {
        await Initialize();
        var state = await Repository.GetPlantStateAsync();
        var plc = (IPlcRepository)Repository;
        await plc.SetPlcSimulationAsync(state.PlcSimulation!.Version, true, true, "test", "Hide response", DateTime.UtcNow);
        var original = Assert.Single(state.PlcCommands);
        await new PlcExchangeService(Repository, plc.PlcChannel, TimeProvider.System).ProcessAsync(original.Id);
        var result = DatabaseBackup.Create(_database.Path, DirectoryPath, "plant-one");
        var restoredPath = Path.Combine(DirectoryPath, "restored.db");
        DatabaseBackup.RestoreCopy(result.DatabasePath, restoredPath, "plant-one");
        using var restoredServices = new ServiceCollection().AddTransportOrdersData(restoredPath).BuildServiceProvider();
        var repo = restoredServices.GetRequiredService<IOrderRepository>(); var restoredPlc = (IPlcRepository)repo;
        var settings = (await repo.GetPlantStateAsync()).PlcSimulation!;
        await restoredPlc.SetPlcSimulationAsync(settings.Version, true, false, "test", "Restore responses", DateTime.UtcNow);
        var exchange = new PlcExchangeService(repo, restoredPlc.PlcChannel, TimeProvider.System);
        Assert.Equal(PlcProgress.Applied, await exchange.ProcessAsync(original.Id));
        Assert.Equal(PlcProgress.Applied, await exchange.ProcessAsync(original.Id));
        var command = (await repo.GetPlantStateAsync()).PlcCommands.Single(c => c.Id == original.Id);
        Assert.Equal(1, command.Attempts);
        Assert.Single(command.History, e => e.Detail == "Arrival and acknowledgement applied atomically");
        await using var context = await restoredServices.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await context.PlcExecutions.CountAsync());
    }
    [Fact]
    public async Task Corruption_wrong_site_and_existing_restore_targets_are_refused()
    {
        await Initialize();
        var backup = DatabaseBackup.Create(_database.Path, DirectoryPath, "plant-one");
        Assert.Throws<InvalidDataException>(() => DatabaseBackup.Verify(backup.DatabasePath, "other-plant"));
        var before = JsonSerializer.Serialize(PlantStateSnapshot.From(await Repository.GetPlantStateAsync()));
        Assert.Throws<IOException>(() => DatabaseBackup.RestoreCopy(backup.DatabasePath, _database.Path, "plant-one"));
        Assert.Equal(before, JsonSerializer.Serialize(PlantStateSnapshot.From(await Repository.GetPlantStateAsync())));
        var target = Path.Combine(DirectoryPath, "reserved.db");
        File.WriteAllText(target + "-wal", "reserved");
        Assert.Throws<IOException>(() => DatabaseBackup.RestoreCopy(backup.DatabasePath, target, "plant-one"));
        File.AppendAllText(backup.DatabasePath, "corruption");
        Assert.Throws<InvalidDataException>(() => DatabaseBackup.Verify(backup.DatabasePath, "plant-one"));
        Assert.Throws<InvalidDataException>(() => DatabaseBackup.RestoreCopy(backup.DatabasePath, target + ".new", "plant-one"));
        Assert.False(File.Exists(target + ".new"));
    }
    public void Dispose() { _services.Dispose(); _database.Dispose(); }

    [Fact]
    public async Task Database_from_an_unknown_application_version_is_not_published_as_a_valid_backup()
    {
        await Initialize();
        using var connection = new SqliteConnection($"Data Source={_database.Path};Pooling=False");
        connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO __EFMigrationsHistory VALUES ('20990101000000_FutureSchema', '99.0.0')";
        command.ExecuteNonQuery();
        Assert.Throws<InvalidDataException>(() => DatabaseBackup.Create(_database.Path, DirectoryPath, "plant-one"));
        Assert.Empty(Directory.GetFiles(DirectoryPath, "plant-*"));
    }
}
