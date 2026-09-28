using System.Globalization;
using System.Windows;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly CultureInfo UkCulture = new("uk-UA");
    private static readonly CultureInfo EnCulture = new("en-US");

    public CultureInfo CurrentCulture { get; private set; } = UkCulture;

    public IReadOnlyList<CultureInfo> SupportedCultures { get; } = new[]
    {
        UkCulture,
        EnCulture
    };

    public event EventHandler<CultureInfo>? CultureChanged;

    public void SetCulture(CultureInfo culture)
    {
        if (Equals(CurrentCulture, culture)) return;

        CurrentCulture = culture;

        var dictUri = culture.TwoLetterISOLanguageName switch
        {
            "en" => new Uri("Resources/Strings.en-US.xaml", UriKind.Relative),
            _ => new Uri("Resources/Strings.uk-UA.xaml", UriKind.Relative)
        };

        var oldDict = Application.Current.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings."));

        var newDict = new ResourceDictionary { Source = dictUri };

        if (oldDict != null)
        {
            Application.Current.Resources.MergedDictionaries.Remove(oldDict);
        }

        Application.Current.Resources.MergedDictionaries.Add(newDict);
        CultureChanged?.Invoke(this, culture);
    }

    public string GetString(string key)
    {
        if (Application.Current.TryFindResource(key) is string value)
        {
            return value;
        }
        return $"[{key}]";
    }
}