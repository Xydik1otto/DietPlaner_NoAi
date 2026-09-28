using System.Globalization;
using System.Net.Mail;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class ValidationService : IValidationService
{
    private readonly ILocalizationService _loc;

    public ValidationService(ILocalizationService loc)
    {
        _loc = loc;
    }

    public IReadOnlyList<string> ValidateRequiredText(string? value, string fieldName, int maxLength)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(string.Format(_loc.GetString("Val_RequiredField"), fieldName));
            return errors;
        }

        if (value.Trim().Length > maxLength)
        {
            errors.Add(string.Format(_loc.GetString("Val_MaxLengthExceeded"), fieldName, maxLength));
        }

        return errors;
    }

    public IReadOnlyList<string> ValidateDecimal(string? value, string fieldName, decimal min, decimal max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [string.Format(_loc.GetString("Val_RequiredField"), fieldName)];
        }

        var normalized = value.Trim().Replace(',', '.');

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatingValue)
            && (double.IsNaN(floatingValue) || double.IsInfinity(floatingValue)))
        {
            return [string.Format(_loc.GetString("Val_InvalidNumber"), fieldName)];
        }

        if (!decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return [string.Format(_loc.GetString("Val_EnterValidNumber"), fieldName)];
        }

        if (number < min || number > max)
        {
            return [string.Format(_loc.GetString("Val_RangeExceeded"), fieldName, min, max)];
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
            return [string.Format(_loc.GetString("Val_InvalidEmailFormat"), fieldName)];
        }
    }

    public IReadOnlyList<string> ValidatePassword(string? value, string fieldName = "Password")
    {
        var errors = ValidateRequiredText(value, fieldName, 128).ToList();
        if (string.IsNullOrWhiteSpace(value))
        {
            return errors;
        }

        if (value.Length < 8)
        {
            errors.Add(string.Format(_loc.GetString("Val_MinPasswordLength"), fieldName, 8));
        }

        return errors;
    }
}