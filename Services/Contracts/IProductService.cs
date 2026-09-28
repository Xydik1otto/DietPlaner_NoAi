using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface IProductService
{
    Task<IReadOnlyList<Product>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<Product> CreateAsync(string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
