using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class SearchService : ISearchService
{
    private readonly IProductService _products;

    public SearchService(IProductService products)
    {
        _products = products;
    }

    public async Task<IReadOnlyList<ProductSearchResult>> SearchProductsAsync(string query, Guid? categoryId = null, decimal? minCalories = null, decimal? maxCalories = null, CancellationToken cancellationToken = default)
    {
        var products = await _products.GetAllAsync(false, cancellationToken);
        if (categoryId.HasValue) products = products.Where(x => x.CategoryId == categoryId.Value).ToList();
        if (minCalories.HasValue) products = products.Where(x => x.Calories >= minCalories.Value).ToList();
        if (maxCalories.HasValue) products = products.Where(x => x.Calories <= maxCalories.Value).ToList();

        // Required Dictionary usage: exact normalized-name index.
        var exactIndex = products
            .GroupBy(p => Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return products.Select(p => new ProductSearchResult(p, 0, false)).ToList();

        var results = new List<ProductSearchResult>();
        if (exactIndex.TryGetValue(normalizedQuery, out var exact))
            results.Add(new ProductSearchResult(exact, 0, true));

        foreach (var product in products)
        {
            if (ReferenceEquals(product, exact)) continue;
            var nameDistance = LevenshteinDistance(normalizedQuery, Normalize(product.Name));
            var categoryText = product.Category?.Name ?? string.Empty;
            var categoryDistance = LevenshteinDistance(normalizedQuery, Normalize(categoryText));
            var caloriesDistance = LevenshteinDistance(normalizedQuery, Normalize(product.Calories.ToString("0.##")));
            var restrictionDistance = product.Allergens.Concat(product.DietaryTags)
                .Select(value => LevenshteinDistance(normalizedQuery, Normalize(value)))
                .DefaultIfEmpty(int.MaxValue)
                .Min();

            var distance = Math.Min(Math.Min(nameDistance, categoryDistance), Math.Min(caloriesDistance, restrictionDistance));
            var threshold = Math.Max(2, normalizedQuery.Length / 3);
            if (distance <= threshold)
                results.Add(new ProductSearchResult(product, distance, false));
        }

        return results
            .OrderBy(x => x.Exact ? 0 : 1)
            .ThenBy(x => x.Distance)
            .ThenBy(x => x.Product.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static int LevenshteinDistance(string left, string right)
    {
        left ??= string.Empty;
        right ??= string.Empty;
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string Normalize(string value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
