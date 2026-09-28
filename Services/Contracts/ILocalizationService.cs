using System.Globalization;

namespace DietPlanner.Services.Contracts;

public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }
    IReadOnlyList<CultureInfo> SupportedCultures { get; }
    event EventHandler<CultureInfo>? CultureChanged;

    void SetCulture(CultureInfo culture);
    string GetString(string key);
}