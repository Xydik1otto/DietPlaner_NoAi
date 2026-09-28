using System.Net;
using System.Net.Mail;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public class EmailService : IEmailService
{
    // Вкажіть ваші дані Gmail / SMTP (для Gmail потрібен "App Password" з налаштувань безпеки Google)
    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;
    private const string SenderEmail = "your-app-email@gmail.com"; // Вкажіть вашу пошту
    private const string SenderPassword = "your-app-password";      // Вкажіть 16-значний пароль застосунку Google

    public async Task SendVerificationCodeAsync(string toEmail, string code, string actionDescription)
    {
        try
        {
            using var message = new MailMessage();
            message.From = new MailAddress(SenderEmail, "DietPlanner Security");
            message.To.Add(toEmail);
            message.Subject = $"Код підтвердження: {code} — DietPlanner";
            message.Body = $"Доброго дня!\n\nВаш 6-значний код для {actionDescription}: {code}\n\nЯкщо ви не запитували цю зміну, ігноруйте цей лист.";

            using var smtp = new SmtpClient(SmtpHost, SmtpPort)
            {
                Credentials = new NetworkCredential(SenderEmail, SenderPassword),
                EnableSsl = true
            };

            await smtp.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Помилка надсилання листа: {ex.Message}");
        }
    }
}