using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DietPlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Data;

public sealed class DbInitializer
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DbInitializer(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);

        await SeedFromJsonSafeAsync(db, cancellationToken);

        if (!await db.DietaryRestrictions.AnyAsync(cancellationToken))
        {
            db.DietaryRestrictions.AddRange(
                new DietModeRestriction("Vegetarian", ["meat", "poultry", "fish", "seafood"]),
                new DietModeRestriction("Vegan", ["meat", "poultry", "fish", "seafood", "dairy", "egg", "honey"]),
                new DietModeRestriction("Pescatarian", ["meat", "poultry"]),
                new DietModeRestriction("Gluten-Free", ["gluten"]),
                new DietModeRestriction("Lactose-Free", ["dairy", "lactose"]),
                new DietModeRestriction("Nut-Free", ["nuts", "peanuts"]),
                new DietModeRestriction("Low-Sodium", ["high-sodium"]));

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedFromJsonSafeAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var categoriesDto = await ReadAndDeserializeAsync<List<CategorySeedDto>>("categories.json", options, cancellationToken) ?? new();
        var productsDto = await ReadAndDeserializeAsync<List<ProductSeedDto>>("products.json", options, cancellationToken) ?? new();
        var dishesDto = await ReadAndDeserializeAsync<List<DishSeedDto>>("dishes.json", options, cancellationToken) ?? new();

        // 1. Створення/отримання Категорій
        var existingCategories = await db.Categories.ToListAsync(cancellationToken);
        var categoryMap = existingCategories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var catDto in categoriesDto)
        {
            if (!categoryMap.ContainsKey(catDto.Name))
            {
                var category = new Category(catDto.Name, catDto.Description);
                db.Categories.Add(category);
                categoryMap[catDto.Name] = category;
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        // 2. Створення/отримання Продуктів
        var existingProducts = await db.Products.ToListAsync(cancellationToken);
        var productMap = existingProducts.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var prodDto in productsDto)
        {
            if (productMap.ContainsKey(prodDto.Name)) continue;
            if (!categoryMap.TryGetValue(prodDto.CategoryName, out var category)) continue;

            var basis = Enum.TryParse<NutritionBasis>(prodDto.Basis, true, out var parsedBasis)
                ? parsedBasis
                : NutritionBasis.Per100Grams;

            var product = new Product(
                prodDto.Name,
                category.Id,
                basis,
                prodDto.Calories,
                prodDto.ProteinG,
                prodDto.FatG,
                prodDto.CarbsG);

            if (prodDto.Allergens != null && prodDto.Allergens.Count > 0)
                product.SetAllergens(prodDto.Allergens);

            if (prodDto.DietaryTags != null && prodDto.DietaryTags.Count > 0)
                product.SetDietaryTags(prodDto.DietaryTags);

            db.Products.Add(product);
            productMap[prodDto.Name] = product;
        }
        await db.SaveChangesAsync(cancellationToken);

        // 3. Створення Страв
        var existingDishes = await db.Dishes.ToListAsync(cancellationToken);
        var existingDishNames = existingDishes.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var dishDto in dishesDto)
        {
            if (existingDishNames.Contains(dishDto.Name)) continue;
            if (!categoryMap.TryGetValue(dishDto.CategoryName, out var category)) continue;

            var dish = new Dish(dishDto.Name, category.Id, dishDto.Description);

            foreach (var ingDto in dishDto.Ingredients)
            {
                if (productMap.TryGetValue(ingDto.ProductName, out var product))
                {
                    dish.Ingredients.Add(new DishIngredient(product.Id, ingDto.Amount, ingDto.Unit));
                }
            }

            db.Dishes.Add(dish);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<T?> ReadAndDeserializeAsync<T>(string fileName, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var jsonPath = ResolveFilePath(fileName);
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath)) return default;

        var jsonContent = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        return JsonSerializer.Deserialize<T>(jsonContent, options);
    }

    private static string? ResolveFilePath(string fileName)
    {
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName)
        };

        return candidatePaths.FirstOrDefault(File.Exists);
    }

    private sealed class CategorySeedDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    private sealed class ProductSeedDto
    {
        public string Name { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string Basis { get; set; } = "Per100Grams";
        public decimal Calories { get; set; }
        public decimal ProteinG { get; set; }
        public decimal FatG { get; set; }
        public decimal CarbsG { get; set; }
        public List<string>? Allergens { get; set; }
        public List<string>? DietaryTags { get; set; }
    }

    private sealed class DishSeedDto
    {
        public string Name { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<IngredientSeedDto> Ingredients { get; set; } = new();
    }

    private sealed class IngredientSeedDto
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Unit { get; set; } = "г";
    }
}