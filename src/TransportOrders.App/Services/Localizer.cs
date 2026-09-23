using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace TransportOrders.App.Services;

/// <summary>
/// Reads texts from Resources/Strings(.de).resx. XAML binds to <see cref="Instance"/> through its indexer,
/// so every bound text updates when the language changes.
/// </summary>
public sealed class Localizer : ILocalizer, INotifyPropertyChanged
{
    public static readonly IReadOnlyList<string> SupportedLanguages = ["en", "de"];

    private static readonly ResourceManager Resources =
        new("TransportOrders.App.Resources.Strings", typeof(Localizer).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("en");

    /// <summary>Shared instance used by XAML bindings (see <see cref="TrExtension"/>) and registered in DI.</summary>
    public static Localizer Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public string Language => _culture.TwoLetterISOLanguageName;

    public string this[string key] => Resources.GetString(key, _culture) ?? $"[{key}]";

    public string Format(string key, params object?[] args) => string.Format(_culture, this[key], args);

    public void SetLanguage(string language)
    {
        var code = SupportedLanguages.Contains(language) ? language : "en";
        if (code == Language)
        {
            return;
        }

        _culture = CultureInfo.GetCultureInfo(code);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
