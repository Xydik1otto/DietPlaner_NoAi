namespace DietPlanner.Services.Contracts;

public interface IEmailService
{
    Task SendVerificationCodeAsync(string toEmail, string code, string actionDescription);
}