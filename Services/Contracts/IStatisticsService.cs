using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DietPlanner.ViewModels;

namespace DietPlanner.Services.Contracts;

public interface IStatisticsService
{
    Task<List<DailyStatItem>> GetDailyStatsAsync(Guid userId, DateTime date);
    Task<List<DailyStatItem>> GetWeeklyStatsAsync(Guid userId, DateTime startDate, DateTime endDate);
    Task<List<DailyStatItem>> GetMonthlyStatsAsync(Guid userId, int year, int month);
}