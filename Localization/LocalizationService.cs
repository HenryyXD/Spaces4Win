using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Spaces4Win.Localization;

public interface ILocalizationService : INotifyPropertyChanged
{
    string LanguagePreference { get; }
    CultureInfo CurrentUiCulture { get; }
    IReadOnlyList<LanguageOption> AvailableLanguages { get; }
    event EventHandler? CultureChanged;

    string this[string key] { get; }
    string Get(string key);
    string Format(string key, params object[] args);
    string Plural(string oneKey, string manyKey, int count);
    void SetLanguagePreference(string preference);
    CultureInfo ResolveUiCulture(string preference);
}

public sealed record LanguageOption(string Id, string DisplayKey);

public sealed class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager Manager =
        new("Spaces4Win.Resources.Strings", typeof(LocalizationService).Assembly);

    private string _preference = "System";
    private CultureInfo _uiCulture = CultureInfo.GetCultureInfo("en");

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CultureChanged;

    public string LanguagePreference => _preference;
    public CultureInfo CurrentUiCulture => _uiCulture;

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new("System", "Language.System"),
        new("en", "Language.English"),
        new("pt-BR", "Language.PortugueseBrazil")
    ];

    /// <summary>Indexer for WPF bindings: Loc[Settings.Overview.Title]</summary>
    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        try
        {
            var value = Manager.GetString(key, _uiCulture);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            value = Manager.GetString(key, CultureInfo.InvariantCulture)
                    ?? Manager.GetString(key, CultureInfo.GetCultureInfo("en"));
            return value ?? $"[{key}]";
        }
        catch
        {
            return $"[{key}]";
        }
    }

    public string Format(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(_uiCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public string Plural(string oneKey, string manyKey, int count)
        => Format(count == 1 ? oneKey : manyKey, count);

    public void SetLanguagePreference(string preference)
    {
        preference = NormalizePreference(preference);
        var culture = ResolveUiCulture(preference);
        _preference = preference;
        ApplyCulture(culture);
    }

    public CultureInfo ResolveUiCulture(string preference)
    {
        preference = NormalizePreference(preference);
        if (preference.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("en");
        }

        if (preference.Equals("pt-BR", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("pt-BR");
        }

        // System
        var system = CultureInfo.CurrentUICulture;
        if (system.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("pt-BR");
        }

        return CultureInfo.GetCultureInfo("en");
    }

    private void ApplyCulture(CultureInfo culture)
    {
        _uiCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        // Keep formatting culture as system for numbers/dates unless we want UI culture:
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentUiCulture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguagePreference)));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizePreference(string preference)
    {
        if (string.IsNullOrWhiteSpace(preference))
        {
            return "System";
        }

        if (preference.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            return "System";
        }

        if (preference.Equals("en", StringComparison.OrdinalIgnoreCase) ||
            preference.Equals("en-US", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        if (preference.Equals("pt-BR", StringComparison.OrdinalIgnoreCase) ||
            preference.Equals("pt", StringComparison.OrdinalIgnoreCase))
        {
            return "pt-BR";
        }

        return "System";
    }
}
