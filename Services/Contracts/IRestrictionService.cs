using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public sealed record RestrictionCheckResult(bool Allowed, string Reason);

public interface IRestrictionService
{
    Task<IReadOnlyList<DietaryRestriction>> GetAvailableAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DietaryRestriction>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetUserRestrictionsAsync(Guid userId, IEnumerable<Guid> restrictionIds, CancellationToken cancellationToken = default);
    Task AddForbiddenProductAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default);
    Task RemoveForbiddenProductAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default);
    Task<RestrictionCheckResult> CheckProductAsync(Guid userId, Product product, CancellationToken cancellationToken = default);
    Task<RestrictionCheckResult> CheckDishAsync(Guid userId, Dish dish, CancellationToken cancellationToken = default);
}
