namespace TransportOrders.Data;

public static class DatabasePaths
{
    /// <summary>
    /// %LOCALAPPDATA%\TransportOrders\transportorders.db — independent of the working directory.
    /// The folder is created if it does not exist yet.
    /// </summary>
    public static string GetDefaultDatabasePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TransportOrders");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "transportorders.db");
    }
}
