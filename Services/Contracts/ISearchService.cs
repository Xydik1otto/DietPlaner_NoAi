using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public sealed record ProductSearchResult(Product Product, int Distance, bool Exact);
public sealed record DishSearchResult(Dish Dish, int Distance, bool Exact);

public interface ISearchService
{
    Task<IReadOnlyList<ProductSearchResult>> SearchProductsAsync(
        string query,
        Guid? categoryId = null,
        decimal? minCalories = null,
        decimal? maxCalories = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSearchResult>> SearchGlobalProductsAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DishSearchResult>> SearchDishesAsync(
        string query,
        Guid? categoryId = null,
        CancellationToken cancellationToken = default);
}
