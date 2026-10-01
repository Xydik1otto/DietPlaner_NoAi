using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface ICategoryService
{
    // For User: global + own personal categories. For Admin: global categories.
    Task<IReadOnlyList<Category>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetGlobalAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<Category> CreateAsync(string name, string? description, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid id, string name, string? description, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
