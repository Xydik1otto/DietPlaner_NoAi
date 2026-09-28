using DietPlanner.Data;
using DietPlanner.Services.Contracts;
using DietPlanner.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public class StatisticsService : IStatisticsService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILocalizationService _loc;

    public StatisticsService(IDbContextFactory<AppDbContext> dbFactory, ILocalizationService loc)
    {
        _dbFactory = dbFactory;
        _loc = loc;
    }

    public async Task<List<DailyStatItem>> GetDailyStatsAsync(Guid userId, DateTime date)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var targetDate = date.Date;

        var intakes = await db.MealIntakes
            .Include(m => m.Items)
            .Where(m => m.UserId == userId && m.ConsumedAtUtc.Date == targetDate)
            .ToListAsync();

        double calories = intakes.Sum(m => m.Items.Sum(i => (double)i.Calories));
        double proteins = intakes.Sum(m => m.Items.Sum(i => (double)i.ProteinG));
        double fats = intakes.Sum(m => m.Items.Sum(i => (double)i.FatG));
        double carbs = intakes.Sum(m => m.Items.Sum(i => (double)i.CarbsG));

        return new List<DailyStatItem>
        {
            new DailyStatItem
            {
                Date = targetDate,
                Calories = calories,
                Proteins = proteins,
                Fats = fats,
                Carbs = carbs,
                GoalCompletionStatus = calories > 0 ? _loc.GetString("Stat_InNorm") : _loc.GetString("Stat_NoRecords")
            }
        };
    }

    public async Task<List<DailyStatItem>> GetWeeklyStatsAsync(Guid userId, DateTime startDate, DateTime endDate)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var intakes = await db.MealIntakes
            .Include(m => m.Items)
            .Where(m => m.UserId == userId && m.ConsumedAtUtc.Date >= startDate.Date && m.ConsumedAtUtc.Date <= endDate.Date)
            .ToListAsync();

        var result = new List<DailyStatItem>();
        for (DateTime day = startDate.Date; day <= endDate.Date; day = day.AddDays(1))
        {
            var dayIntakes = intakes.Where(m => m.ConsumedAtUtc.Date == day).ToList();

            double cal = dayIntakes.Sum(m => m.Items.Sum(i => (double)i.Calories));
            double prot = dayIntakes.Sum(m => m.Items.Sum(i => (double)i.ProteinG));
            double fat = dayIntakes.Sum(m => m.Items.Sum(i => (double)i.FatG));
            double carbs = dayIntakes.Sum(m => m.Items.Sum(i => (double)i.CarbsG));

            result.Add(new DailyStatItem
            {
                Date = day,
                Calories = cal,
                Proteins = prot,
                Fats = fat,
                Carbs = carbs,
                GoalCompletionStatus = dayIntakes.Any() ? _loc.GetString("Stat_Completed") : _loc.GetString("Stat_Skipped")
            });
        }

        return result;
    }

    public async Task<List<DailyStatItem>> GetMonthlyStatsAsync(Guid userId, int year, int month)
    {
        DateTime start = new DateTime(year, month, 1);
        DateTime end = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return await GetWeeklyStatsAsync(userId, start, end);
    }
}