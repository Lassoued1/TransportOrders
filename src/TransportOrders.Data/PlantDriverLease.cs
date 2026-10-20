using TransportOrders.Core;

namespace TransportOrders.Data;

/// <summary>File leases are released by the OS on process termination, including crashes.</summary>
public sealed class PlantDriverLease(string databasePath) : IPlantExecutionGate
{
    private readonly string _path = Path.GetFullPath(databasePath) + ".driver.lock";
    public IDisposable? TryEnter()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        try { return new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }
}
