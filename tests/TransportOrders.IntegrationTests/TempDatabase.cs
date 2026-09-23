using Microsoft.Data.Sqlite;

namespace TransportOrders.IntegrationTests;

/// <summary>A throw-away SQLite file in the temp folder, deleted with its WAL files on dispose.</summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _folder =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TransportOrders.Tests", Guid.NewGuid().ToString("N"));

    public TempDatabase()
    {
        Directory.CreateDirectory(_folder);
        Path = System.IO.Path.Combine(_folder, "transportorders.db");
    }

    public string Path { get; }

    public void Dispose()
    {
        // Pooled connections keep the file open; release them before deleting.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp file must not fail the test run.
        }
    }
}
