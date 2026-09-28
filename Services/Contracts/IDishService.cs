using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public sealed record DishIngredientInput(Guid ProductId, decimal Amount, string Unit);

public interface IDishService
{
    Task<IReadOnlyList<Dish>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<Dish> CreateAsync(string name, Guid categoryId, string? description, IEnumerable<DishIngredientInput> ingredients, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, IEnumerable<DishIngredientInput> ingredients, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
