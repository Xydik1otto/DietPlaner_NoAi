using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class SearchService : ISearchService
{
    private readonly IProductService _products;
    private readonly IDishService _dishes;

    public SearchService(IProductService products, IDishService dishes)
    {
        _products = products;
        _dishes = dishes;
    }

    public async Task<IReadOnlyList<ProductSearchResult>> SearchProductsAsync(
        string query,
        Guid? categoryId = null,
        decimal? minCalories = null,
        decimal? maxCalories = null,
        CancellationToken cancellationToken = default)
    {
        var products = await _products.GetAllAsync(false, cancellationToken);
        return SearchProducts(products, query, categoryId, minCalories, maxCalories);
    }

    public async Task<IReadOnlyList<ProductSearchResult>> SearchGlobalProductsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var products = await _products.GetGlobalAsync(false, cancellationToken);
        return SearchProducts(products, query);
    }

    private static IReadOnlyList<ProductSearchResult> SearchProducts(
        IEnumerable<Product> source,
        string query,
        Guid? categoryId = null,
        decimal? minCalories = null,
        decimal? maxCalories = null)
    {
        var products = source.AsEnumerable();

        if (categoryId.HasValue)
            products = products.Where(x => x.CategoryId == categoryId.Value);
        if (minCalories.HasValue)
            products = products.Where(x => x.Calories >= minCalories.Value);
        if (maxCalories.HasValue)
            products = products.Where(x => x.Calories <= maxCalories.Value);

        var productList = products.ToList();
        var normalizedQuery = Normalize(query);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return productList
                .Select(p => new ProductSearchResult(p, 0, false))
                .OrderBy(x => x.Product.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        // Keep the old exact-name behavior, then extend it with substring and
        // Levenshtein matching. This means an existing search such as "ban"
        // still finds "Banana", while a typo such as "banan" / "bananna"
        // can also be matched by edit distance.
        var exactIndex = productList
            .GroupBy(p => Normalize(p.Name))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        exactIndex.TryGetValue(normalizedQuery, out var exact);

        var results = new List<ProductSearchResult>();
        foreach (var product in productList)
        {
            var name = Normalize(product.Name);
            var category = Normalize(product.Category?.Name);
            var calories = Normalize(product.Calories.ToString("0.##"));
            var restrictions = product.Allergens.Concat(product.DietaryTags)
                .Select(Normalize)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var nameContains = name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            var categoryContains = category.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            var caloriesContains = calories.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            var restrictionContains = restrictions.Any(x => x.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));

            var nameDistance = LevenshteinDistance(normalizedQuery, name);
            var categoryDistance = string.IsNullOrEmpty(category)
                ? int.MaxValue
                : LevenshteinDistance(normalizedQuery, category);
            var caloriesDistance = LevenshteinDistance(normalizedQuery, calories);
            var restrictionDistance = restrictions.Count == 0
                ? int.MaxValue
                : restrictions.Select(x => LevenshteinDistance(normalizedQuery, x)).Min();

            var distance = Math.Min(
                Math.Min(nameDistance, categoryDistance),
                Math.Min(caloriesDistance, restrictionDistance));

            var threshold = Math.Max(2, normalizedQuery.Length / 3);
            var fuzzyMatch = distance <= threshold;
            var containsMatch = nameContains || categoryContains || caloriesContains || restrictionContains;
            var isExact = exact != null && product.Id == exact.Id;

            if (isExact || containsMatch || fuzzyMatch)
            {
                // A substring is considered a perfect search match for ordering,
                // but only an exact normalized name is marked as Exact=true.
                var effectiveDistance = containsMatch || isExact ? 0 : distance;
                results.Add(new ProductSearchResult(product, effectiveDistance, isExact));
            }
        }

        return results
            .OrderBy(x => x.Exact ? 0 : 1)
            .ThenBy(x => x.Distance)
            .ThenBy(x => x.Product.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }


    public async Task<IReadOnlyList<DishSearchResult>> SearchDishesAsync(
        string query,
        Guid? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var dishes = await _dishes.GetAllAsync(false, cancellationToken);
        var source = dishes.AsEnumerable();

        if (categoryId.HasValue)
            source = source.Where(x => x.CategoryId == categoryId.Value);

        var dishList = source.ToList();
        var normalizedQuery = Normalize(query);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return dishList
                .Select(d => new DishSearchResult(d, 0, false))
                .OrderBy(x => x.Dish.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        var results = new List<DishSearchResult>();
        foreach (var dish in dishList)
        {
            var name = Normalize(dish.Name);
            var nameDistance = LevenshteinDistance(normalizedQuery, name);
            var bestIngredientDistance = dish.Ingredients
                .Where(i => i.Product != null)
                .Select(i => LevenshteinDistance(normalizedQuery, Normalize(i.Product!.Name)))
                .DefaultIfEmpty(int.MaxValue)
                .Min();

            var nameContains = name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
            var ingredientContains = dish.Ingredients.Any(i =>
                i.Product != null &&
                Normalize(i.Product.Name).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));

            var distance = Math.Min(nameDistance, bestIngredientDistance);
            var threshold = Math.Max(2, normalizedQuery.Length / 3);
            var fuzzyMatch = distance <= threshold;
            var containsMatch = nameContains || ingredientContains;
            var isExact = string.Equals(name, normalizedQuery, StringComparison.OrdinalIgnoreCase);

            if (isExact || containsMatch || fuzzyMatch)
            {
                var effectiveDistance = isExact || containsMatch ? 0 : distance;
                results.Add(new DishSearchResult(dish, effectiveDistance, isExact));
            }
        }

        return results
            .OrderBy(x => x.Exact ? 0 : 1)
            .ThenBy(x => x.Distance)
            .ThenBy(x => x.Dish.Name, StringComparer.CurrentCultureIgnoreCase)
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
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
