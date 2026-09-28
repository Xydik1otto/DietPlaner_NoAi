using DietPlanner.ViewModels;

namespace DietPlanner.Services.Contracts;

public interface IMealIntakeService
{
    Task AddIntakeItemAsync(Guid userId, Guid? productId, Guid? dishId, decimal amountGrams, CancellationToken cancellationToken = default);
    Task<List<MealIntakeDisplayDto>> GetTodayIntakesAsync(Guid userId, CancellationToken cancellationToken = default);
    Task DeleteIntakeItemByIdAsync(Guid itemId, Guid userId, CancellationToken cancellationToken = default);
    Task UpdateIntakeItemAmountAsync(Guid itemId, decimal newAmountGrams, Guid userId, CancellationToken cancellationToken = default);
    Task RemoveIntakeItemByFoodAsync(Guid userId, Guid? productId, Guid? dishId, string itemName, CancellationToken cancellationToken = default);
    
    // Передаємо excludeIds для ігнорування вже показаних варіантів
    Task<List<FoodItemDisplayDto>> SuggestMealOptionsAsync(
        Guid userId, 
        int limit = 5, 
        IEnumerable<Guid>? excludeIds = null, 
        CancellationToken cancellationToken = default);
    Task SyncPastDaysEatenItemsAsync(Guid userId, CancellationToken cancellationToken = default);
}