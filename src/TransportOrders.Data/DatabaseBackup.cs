using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TransportOrders.Data;

public sealed record BackupManifest(int FormatVersion, string PlantId, DateTime CreatedUtc,
    string Sha256, long Bytes, string[] Migrations);
public sealed record DatabaseBackupResult(string DatabasePath, string ManifestPath, BackupManifest Manifest);

/// <summary>SQLite online backup; restored copies never overwrite an existing installation.</summary>
public static class DatabaseBackup
{
    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    private static string[] Check(SqliteConnection connection)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        if (!Equals(check.ExecuteScalar(), "ok")) throw new InvalidDataException("SQLite integrity check failed.");
        check.CommandText = "PRAGMA foreign_key_check";
        using (var reader = check.ExecuteReader())
            if (reader.Read()) throw new InvalidDataException("SQLite foreign key check failed.");
        check.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        var migrations = new List<string>();
        using (var reader = check.ExecuteReader())
            while (reader.Read()) migrations.Add(reader.GetString(0));
        if (migrations.Count == 0) throw new InvalidDataException("Missing plant schema migrations.");
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        var supported = context.Database.GetMigrations().Order(StringComparer.Ordinal).ToArray();
        if (!migrations.SequenceEqual(supported.Take(migrations.Count)) || migrations.Count > supported.Length)
            throw new InvalidDataException("Plant schema is incomplete or requires a different application version.");
        return migrations.ToArray();
    }
    public static DatabaseBackupResult Create(string sourcePath, string directory, string plantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plantId);
        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The plant database must exist.", sourcePath);
        Directory.CreateDirectory(directory);
        var name = $"plant-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db";
        var output = Path.GetFullPath(Path.Combine(directory, name));
        var temporary = output + ".partial";
        var manifestPath = output + ".json";
        try
        {
            string[] migrations;
            using (var source = Open(sourcePath, SqliteOpenMode.ReadOnly))
            using (var target = Open(temporary, SqliteOpenMode.ReadWriteCreate))
            {
                source.BackupDatabase(target);
                // A standalone backup must not depend on a WAL alongside the backup file.
                using var journal = target.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode=DELETE";
                journal.ExecuteScalar();
                migrations = Check(target);
            }
            using var bytes = File.OpenRead(temporary);
            var manifest = new BackupManifest(1, plantId, DateTime.UtcNow,
                Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, migrations);
            bytes.Dispose();
            File.WriteAllText(manifestPath + ".partial", JsonSerializer.Serialize(manifest));
            File.Move(temporary, output);
            File.Move(manifestPath + ".partial", manifestPath);
            return new(output, manifestPath, manifest);
        }
        catch
        {
            // Only paths created by this invocation are removed.
            foreach (var file in new[] { temporary, temporary + "-wal", temporary + "-shm", output, manifestPath + ".partial", manifestPath })
                if (File.Exists(file)) File.Delete(file);
            throw;
        }
    }
    public static BackupManifest Verify(string backupPath, string expectedPlantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPlantId);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(backupPath + ".json"))
            ?? throw new InvalidDataException("Missing backup manifest.");
        if (manifest.FormatVersion != 1 || !string.Equals(manifest.PlantId, expectedPlantId, StringComparison.Ordinal))
            throw new InvalidDataException("Backup contract or installation identity does not match.");
        using var bytes = File.OpenRead(backupPath);
        if (bytes.Length != manifest.Bytes || !string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), manifest.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Backup checksum does not match.");
        using var connection = Open(backupPath, SqliteOpenMode.ReadOnly);
        if (!Check(connection).SequenceEqual(manifest.Migrations)) throw new InvalidDataException("Backup schema does not match the manifest.");
        return manifest;
    }
    public static BackupManifest RestoreCopy(string backupPath, string destination, string expectedPlantId)
    {
        backupPath = Path.GetFullPath(backupPath); destination = Path.GetFullPath(destination);
        // Existing SQLite journal files also identify a destination that must not be replaced.
        if (new[] { destination, destination + "-wal", destination + "-shm", destination + "-journal" }.Any(File.Exists))
            throw new IOException("Restore requires a new database path with no existing journal files.");
        using var source = File.OpenRead(backupPath);
        var manifest = Verify(backupPath, expectedPlantId);
        var created = false;
        try
        {
            using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { created = true; source.CopyTo(target); target.Flush(flushToDisk: true); }
            using var restored = Open(destination, SqliteOpenMode.ReadOnly);
            if (!Check(restored).SequenceEqual(manifest.Migrations)) throw new InvalidDataException("Restored schema check failed.");
            return manifest;
        }
        catch
        {
            if (created && File.Exists(destination)) File.Delete(destination);
            throw;
        }
    }
}
