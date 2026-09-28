using Microsoft.EntityFrameworkCore;
using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public class AppStartupService : IAppStartupService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IOpenFoodFactsService _openFoodFactsService;
    private readonly ILoggingService _logging;

    public AppStartupService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IOpenFoodFactsService openFoodFactsService,
        ILoggingService logging)
    {
        _dbContextFactory = dbContextFactory;
        _openFoodFactsService = openFoodFactsService;
        _logging = logging;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // 1. Гарантуємо створення БД
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // 2. Гарантуємо наявність базових категорій
        var categoryMap = await EnsureDefaultCategoriesAsync(db, cancellationToken);

        // 3. Перевіряємо кількість продуктів у базі
        var existingProductCount = await db.Products.CountAsync(cancellationToken);

        // Якщо база порожня або містить менше 15 продуктів — підтягуємо базовий асортимент з API
        if (existingProductCount < 15)
        {
            await SeedInitialProductsFromApiAsync(db, categoryMap, cancellationToken);
        }
    }

    private async Task<Dictionary<string, Category>> EnsureDefaultCategoriesAsync(
        AppDbContext db, 
        CancellationToken cancellationToken)
    {
        var defaultCategoryNames = new[]
        {
            "Крупи, каші та злаки",
            "Молочні продукти та сири",
            "М'ясо, птиця та риба",
            "Фрукти, ягоди та овочі",
            "Перекуси, горіхи та ласощі",
            "Загальне"
        };

        var existingCategories = await db.Categories.ToListAsync(cancellationToken);
        var categoryMap = existingCategories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in defaultCategoryNames)
        {
            if (!categoryMap.ContainsKey(name))
            {
                var category = new Category(name, $"Категорія {name}");
                db.Categories.Add(category);
                categoryMap[name] = category;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return categoryMap;
    }

    private async Task SeedInitialProductsFromApiAsync(
        AppDbContext db, 
        Dictionary<string, Category> categoryMap, 
        CancellationToken cancellationToken)
    {
        // Ключові продукти українського раціону для первинного імпорту
        var searchQueries = new (string Query, string CategoryName)[]
        {
            ("вівсянка", "Крупи, каші та злаки"),
            ("гречка", "Крупи, каші та злаки"),
            ("рис", "Крупи, каші та злаки"),
            ("творог", "Молочні продукти та сири"),
            ("йогурт", "Молочні продукти та сири"),
            ("сир", "Молочні продукти та сири"),
            ("куряче філе", "М'ясо, птиця та риба"),
            ("індичка", "М'ясо, птиця та риба"),
            ("яблуко", "Фрукти, ягоди та овочі"),
            ("банан", "Фрукти, ягоди та овочі"),
            ("горіхи", "Перекуси, горіхи та ласощі"),
            ("хлібці", "Перекуси, горіхи та ласощі")
        };

        var existingNames = new HashSet<string>(
            await db.Products.Select(p => p.Name).ToListAsync(cancellationToken), 
            StringComparer.OrdinalIgnoreCase);

        var newProducts = new List<Product>();
        var defaultCategory = categoryMap.Values.First();

        foreach (var (query, categoryName) in searchQueries)
        {
            try
            {
                var apiResults = await _openFoodFactsService.SearchAsync(query, cancellationToken);
                if (apiResults == null || apiResults.Count == 0) continue;

                var targetCategory = categoryMap.TryGetValue(categoryName, out var cat) ? cat : defaultCategory;

                // Беремо перші 2-3 якісні результати з API для кожного запиту
                foreach (var item in apiResults.Take(3))
                {
                    if (string.IsNullOrWhiteSpace(item.Name) || existingNames.Contains(item.Name)) 
                        continue;

                    // Ігноруємо товари з некоректними калоріями
                    if (item.Calories <= 0m) continue;

                    var product = new Product(
                        item.Name,
                        targetCategory.Id,
                        NutritionBasis.Per100Grams,
                        item.Calories,
                        item.Proteins,
                        item.Fats,
                        item.Carbs);

                    if (!string.IsNullOrWhiteSpace(item.BrandOrCategory))
                    {
                        product.SetDescription($"Бренд/Категорія: {item.BrandOrCategory}");
                    }

                    newProducts.Add(product);
                    existingNames.Add(item.Name);
                }
            }
            catch
            {
                // Помилка одного запиту не зупиняє ініціалізацію решти
            }
        }

        if (newProducts.Count > 0)
        {
            db.Products.AddRange(newProducts);
            await db.SaveChangesAsync(cancellationToken);

            await _logging.LogActionAsync(
                Guid.Empty,
                ActionType.Create,
                $"Автоматично імпортовано {newProducts.Count} продуктів з OpenFoodFacts API при першому запуску.",
                entityName: nameof(Product),
                cancellationToken: cancellationToken);
        }
    }
}