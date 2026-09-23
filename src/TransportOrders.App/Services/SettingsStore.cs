using System.IO;
using System.Text.Json;

namespace TransportOrders.App.Services;

/// <summary>Per-user preferences in %LOCALAPPDATA%\TransportOrders\settings.json. Failures fall back to defaults.</summary>
public sealed class SettingsStore(string path)
{
    public string? LoadLanguage()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))?.Language : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void SaveLanguage(string language)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new AppSettings(language)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that cannot be saved is not worth interrupting the user.
        }
    }

    private sealed record AppSettings(string? Language);
}
