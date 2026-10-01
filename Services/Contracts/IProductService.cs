using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface IProductService
{
    // For the current catalog screen: personal catalog for User, global catalog for Admin.
    Task<IReadOnlyList<Product>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetGlobalAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetForUserAsync(Guid userId, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task AddToPersonalCatalogAsync(Guid productId, Guid? userId = null, CancellationToken cancellationToken = default);
    Task RemoveFromPersonalCatalogAsync(Guid productId, Guid? userId = null, CancellationToken cancellationToken = default);

    Task<Product> CreateAsync(string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
