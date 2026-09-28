namespace DietPlanner.Services.Contracts;

public interface IValidationService
{
    IReadOnlyList<string> ValidateRequiredText(string? value, string fieldName, int maxLength);
    IReadOnlyList<string> ValidateDecimal(string? value, string fieldName, decimal min, decimal max);
    IReadOnlyList<string> ValidateEmail(string? value, string fieldName = "Email");
    IReadOnlyList<string> ValidatePassword(string? value, string fieldName = "Пароль");
}
