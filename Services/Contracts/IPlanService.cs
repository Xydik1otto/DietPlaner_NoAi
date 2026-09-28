using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public sealed record PlanGenerationResult(
    bool Success,
    string Message,
    NutritionPlan? Plan,
    NutritionDeviation? Deviation,
    IReadOnlyList<string> Alternatives);

public interface IPlanService
{
    Task<PlanGenerationResult> GenerateAsync(Guid userId, int mealCount, decimal? targetCalories = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanGenerationResult>> GenerateWeekAsync(Guid userId, int mealCount, decimal? targetCalories = null, CancellationToken cancellationToken = default);
    Task<NutritionPlan?> GetForDateAsync(Guid userId, DateTime date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NutritionPlan>> GetHistoryAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ReplacePlanItemAsync(Guid planItemId, Guid? newProductId, Guid? newDishId, decimal portionGrams, CancellationToken cancellationToken = default);
    
}