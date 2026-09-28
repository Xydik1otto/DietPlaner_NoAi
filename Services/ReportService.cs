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
    private readonly ILocalizationService _loc;
    private const int MaxReportFiles = 30;

    public ReportService(IDbContextFactory<AppDbContext> dbContextFactory, INutritionCalculator calculator, ILocalizationService loc)
    {
        _dbContextFactory = dbContextFactory;
        _calculator = calculator;
        _loc = loc;
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
        }

        var today = DateTime.UtcNow.Date;
        var todayIntakes = await db.MealIntakes
            .Include(i => i.Items)
            .Where(i => i.UserId == userId && i.ConsumedAtUtc.Date == today)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine(_loc.GetString("Rpt_HeaderTitle"));
        sb.AppendLine("==================================================");
        sb.AppendLine($"{_loc.GetString("Rpt_GenDate")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{_loc.GetString("Rpt_User")}: {user.DisplayName} ({user.Email})");
        
        if (calc.HasValue)
        {
            sb.AppendLine(string.Format(_loc.GetString("Rpt_BodyMetrics"), user.WeightKg, user.HeightCm, calc.Value.BodyMassIndex));
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"{_loc.GetString("Rpt_DailyTarget")}: {calc.Value.Targets.Calories:F0} {_loc.GetString("Unit_Kcal")}");
            sb.AppendLine(string.Format(_loc.GetString("Rpt_MacrosTarget"), calc.Value.Targets.ProteinG, calc.Value.Targets.FatG, calc.Value.Targets.CarbsG));
        }

        sb.AppendLine("==================================================");
        sb.AppendLine(_loc.GetString("Rpt_TodayIntakesHeader"));

        decimal totalCal = 0, totalP = 0, totalF = 0, totalC = 0;
        foreach (var intake in todayIntakes)
        {
            foreach (var item in intake.Items)
            {
                sb.AppendLine($"- [{intake.ConsumedAtUtc.ToLocalTime():HH:mm}] {item.ItemName} ({item.Amount:F0}{_loc.GetString("Unit_Grams")}) — {item.Calories:F0} {_loc.GetString("Unit_Kcal")} (Б:{item.ProteinG:F1}г, Ж:{item.FatG:F1}г, В:{item.CarbsG:F1}г)");
                totalCal += item.Calories;
                totalP += item.ProteinG;
                totalF += item.FatG;
                totalC += item.CarbsG;
            }
        }

        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine(string.Format(_loc.GetString("Rpt_FactSummary"), totalCal, totalP, totalF, totalC));
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
        }
    }
}