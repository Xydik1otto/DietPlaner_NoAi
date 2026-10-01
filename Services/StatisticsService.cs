using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using DietPlanner.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public class StatisticsService : IStatisticsService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILocalizationService _loc;
    private readonly SessionService _session;

    public StatisticsService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILocalizationService loc,
        SessionService session)
    {
        _dbFactory = dbFactory;
        _loc = loc;
        _session = session;
    }

    public async Task<List<DailyStatItem>> GetDailyStatsAsync(Guid userId, DateTime date)
    {
        EnsureCanViewUserStats(userId);
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
        EnsureCanViewUserStats(userId);
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
        EnsureCanViewUserStats(userId);
        DateTime start = new DateTime(year, month, 1);
        DateTime end = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return await GetWeeklyStatsAsync(userId, start, end);
    }
    public async Task<AdminStatisticsSnapshot> GetAdminStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser?.Role != UserRole.Admin)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var totalUsers = await db.Users.CountAsync(cancellationToken);
        var usersWithLogin = await db.Users.CountAsync(x => x.LastLoginAtUtc.HasValue, cancellationToken);
        var totalPlans = await db.NutritionPlans.CountAsync(cancellationToken);

        return new AdminStatisticsSnapshot(totalUsers, usersWithLogin, totalPlans);
    }


    private void EnsureCanViewUserStats(Guid userId)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser is null)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));

        if (_session.CurrentUser.Role != UserRole.Admin && _session.CurrentUser.Id != userId)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }
}
