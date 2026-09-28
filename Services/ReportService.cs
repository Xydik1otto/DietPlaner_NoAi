using System.IO;
using System.Text;
using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public class ReportService : IReportService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly INutritionCalculator _calculator;
    private const int MaxReportFiles = 30;

    public ReportService(IDbContextFactory<AppDbContext> dbContextFactory, INutritionCalculator calculator)
    {
        _dbContextFactory = dbContextFactory;
        _calculator = calculator;
    }

    public async Task<string> GenerateUserReportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user == null) return string.Empty;

        NutritionCalculation? calc = null;
        try
        {
            calc = _calculator.Calculate(user);
        }
        catch
        {
            // Якщо профіль ще не заповнений повністю
        }

        var today = DateTime.UtcNow.Date;
        var todayIntakes = await db.MealIntakes
            .Include(i => i.Items)
            .Where(i => i.UserId == userId && i.ConsumedAtUtc.Date == today)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine("           ЗВІТ ХАРЧУВАННЯ — DietPlanner          ");
        sb.AppendLine("==================================================");
        sb.AppendLine($"Дата генерації: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Користувач: {user.DisplayName} ({user.Email})");
        
        if (calc.HasValue)
        {
            sb.AppendLine($"Вага: {user.WeightKg} кг | Зріст: {user.HeightCm} см | ІМТ: {calc.Value.BodyMassIndex:F1}");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"ДОБОВА ЦІЛЬ: {calc.Value.Targets.Calories:F0} ккал");
            sb.AppendLine($"Білки: {calc.Value.Targets.ProteinG:F1}г | Жири: {calc.Value.Targets.FatG:F1}г | Вуглеводи: {calc.Value.Targets.CarbsG:F1}г");
        }

        sb.AppendLine("==================================================");
        sb.AppendLine("СПИСОК ПРИЙОМІВ ЇЖІ ЗА СЬОГОДНІ:");

        decimal totalCal = 0, totalP = 0, totalF = 0, totalC = 0;
        foreach (var intake in todayIntakes)
        {
            foreach (var item in intake.Items)
            {
                sb.AppendLine($"- [{intake.ConsumedAtUtc.ToLocalTime():HH:mm}] {item.ItemName} ({item.Amount:F0}г) — {item.Calories:F0} ккал (Б:{item.ProteinG:F1}г, Ж:{item.FatG:F1}г, В:{item.CarbsG:F1}г)");
                totalCal += item.Calories;
                totalP += item.ProteinG;
                totalF += item.FatG;
                totalC += item.CarbsG;
            }
        }

        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine($"ФАКТИЧНО СБАТУРАНО: {totalCal:F0} ккал | Б:{totalP:F1}г | Ж:{totalF:F1}г | В:{totalC:F1}г");
        sb.AppendLine("==================================================");

        var reportsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
        Directory.CreateDirectory(reportsFolder);

        var fileName = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        var filePath = Path.Combine(reportsFolder, fileName);

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, cancellationToken);
        CleanupOldReports(reportsFolder);

        return filePath;
    }

    private static void CleanupOldReports(string folderPath)
    {
        try
        {
            var files = new DirectoryInfo(folderPath)
                .GetFiles("report_*.txt")
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            if (files.Count > MaxReportFiles)
            {
                foreach (var file in files.Skip(MaxReportFiles))
                {
                    file.Delete();
                }
            }
        }
        catch
        {
            // Ігноруємо помилки видалення
        }
    }
}