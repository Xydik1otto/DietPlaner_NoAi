using System.Globalization;
using System.Net.Mail;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class ValidationService : IValidationService
{
    public IReadOnlyList<string> ValidateRequiredText(string? value, string fieldName, int maxLength)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{fieldName}: поле не може бути порожнім.");
            return errors;
        }

        if (value.Trim().Length > maxLength)
        {
            errors.Add($"{fieldName}: максимальна довжина — {maxLength} символів.");
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateDecimal(string? value, string fieldName, decimal min, decimal max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [$"{fieldName}: поле не може бути порожнім."];
        }

        var normalized = value.Trim();

        // decimal itself has no NaN/Infinity values, but the requirement applies
        // to user input as well, so check the textual numeric value at the boundary.
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out var floatingValue)
            && (double.IsNaN(floatingValue) || double.IsInfinity(floatingValue)))
        {
            return [$"{fieldName}: значення NaN або нескінченність неприпустимі."];
        }

        if (!decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out var number))
        {
            return [$"{fieldName}: введіть число."];
        }

        if (number < min || number > max)
        {
            return [$"{fieldName}: значення має бути в межах {min}–{max}."];
        }

        return Array.Empty<string>();
    }

    public IReadOnlyList<string> ValidateEmail(string? value, string fieldName = "Email")
    {
        var required = ValidateRequiredText(value, fieldName, 320).ToList();
        if (required.Count > 0)
        {
            return required;
        }

        try
        {
            _ = new MailAddress(value!.Trim());
            return Array.Empty<string>();
        }
        catch (FormatException)
        {
            return [$"{fieldName}: некоректний формат email."];
        }
    }

    public IReadOnlyList<string> ValidatePassword(string? value, string fieldName = "Пароль")
    {
        var errors = ValidateRequiredText(value, fieldName, 128).ToList();
        if (string.IsNullOrWhiteSpace(value))
        {
            return errors;
        }

        if (value.Length < 8)
        {
            errors.Add($"{fieldName}: мінімальна довжина — 8 символів.");
        }

        return errors;
    }
}
