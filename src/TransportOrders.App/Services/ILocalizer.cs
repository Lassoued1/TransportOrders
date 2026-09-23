namespace TransportOrders.App.Services;

/// <summary>UI texts in the current language (English or German), switchable at runtime.</summary>
public interface ILocalizer
{
    string this[string key] { get; }

    /// <summary>Two-letter code of the current language: "en" or "de".</summary>
    string Language { get; }

    string Format(string key, params object?[] args);

    void SetLanguage(string language);

    event EventHandler? LanguageChanged;
}
