using System.Text.Json;
using TransportOrders.Data;

namespace TransportOrders.Api;

public static class MaintenanceCommand
{
    public static bool TryRun(string[] args)
    {
        if (!args.Contains("--maintenance", StringComparer.Ordinal)) return false;
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            if (args.Length % 2 != 0) throw new ArgumentException("Maintenance arguments must be name/value pairs.");
            for (var i = 0; i < args.Length; i += 2) options.Add(args[i], args[i + 1]);
            string Required(string key) => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException($"Required argument: {key}");
            var operation = Required("--maintenance");
            var plant = Required("--plant-id");
            object result = operation switch
            {
                "backup" => DatabaseBackup.Create(Required("--database"), Required("--destination"), plant),
                "verify" => DatabaseBackup.Verify(Required("--backup"), plant),
                "restore-copy" => DatabaseBackup.RestoreCopy(Required("--backup"), Required("--destination"), plant),
                _ => throw new ArgumentException("Use backup, verify or restore-copy.")
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
        return true;
    }
}
