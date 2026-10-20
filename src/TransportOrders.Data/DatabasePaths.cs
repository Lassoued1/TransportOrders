namespace TransportOrders.Data;

public static class DatabasePaths
{
    /// <summary>
    /// Prefer an existing shared ProgramData database; otherwise use the user's demo database.
    /// </summary>
    public static string GetDefaultDatabasePath()
    {
        var configured = Environment.GetEnvironmentVariable("TRANSPORTORDERS_DATABASE_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TransportOrders", "extrusion.db");
        if (File.Exists(shared)) return shared;
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TransportOrders");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "extrusion.db");
    }
}
