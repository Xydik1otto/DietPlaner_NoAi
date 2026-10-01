using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class SearchServiceTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("banana", "banana", 0)]
    public void LevenshteinDistance_ReturnsExpectedDistance(string left, string right, int expected)
    {
        Assert.Equal(expected, SearchService.LevenshteinDistance(left, right));
    }

    [Fact]
    public async Task SearchProductsAsync_FindsTypoByLevenshtein()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var banana = TestDataFactory.CreateProduct(category.Id, "Banana", 89m);
        var chicken = TestDataFactory.CreateProduct(category.Id, "Chicken", 165m);
        var service = new SearchService(new FakeProductService([banana, chicken], [banana, chicken]), new FakeDishService([]));

        var results = await service.SearchProductsAsync("bananna");

        Assert.Single(results);
        Assert.Equal("Banana", results[0].Product.Name);
        Assert.False(results[0].Exact);
        Assert.Equal(1, results[0].Distance);
    }

    [Fact]
    public async Task SearchProductsAsync_KeepsSubstringSearch()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var banana = TestDataFactory.CreateProduct(category.Id, "Banana", 89m);
        var service = new SearchService(new FakeProductService([banana], [banana]), new FakeDishService([]));

        var results = await service.SearchProductsAsync("nan");

        Assert.Single(results);
        Assert.Equal("Banana", results[0].Product.Name);
        Assert.Equal(0, results[0].Distance);
    }

    [Fact]
    public async Task SearchProductsAsync_AppliesCategoryAndCaloriesFilters()
    {
        var categoryA = TestDataFactory.CreateGlobalCategory("A");
        var categoryB = TestDataFactory.CreateGlobalCategory("B");
        var low = TestDataFactory.CreateProduct(categoryA.Id, "Apple", 50m);
        var high = TestDataFactory.CreateProduct(categoryA.Id, "Avocado", 160m);
        var otherCategory = TestDataFactory.CreateProduct(categoryB.Id, "Apple Sauce", 60m);
        var service = new SearchService(new FakeProductService([low, high, otherCategory], [low, high, otherCategory]), new FakeDishService([]));

        var results = await service.SearchProductsAsync("apple", categoryA.Id, minCalories: 40m, maxCalories: 80m);

        Assert.Single(results);
        Assert.Equal(low.Id, results[0].Product.Id);
    }

    [Fact]
    public async Task SearchProductsAsync_EmptyQueryReturnsAllFilteredProducts()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var a = TestDataFactory.CreateProduct(category.Id, "Apple", 50m);
        var b = TestDataFactory.CreateProduct(category.Id, "Banana", 90m);
        var service = new SearchService(new FakeProductService([b, a], [b, a]), new FakeDishService([]));

        var results = await service.SearchProductsAsync("   ");

        Assert.Equal(["Apple", "Banana"], results.Select(x => x.Product.Name).ToArray());
        Assert.All(results, result => Assert.False(result.Exact));
    }

    [Fact]
    public async Task SearchGlobalProductsAsync_UsesGlobalSource()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var personal = TestDataFactory.CreateProduct(category.Id, "Personal Apple");
        var global = TestDataFactory.CreateProduct(category.Id, "Global Apple");
        var fake = new FakeProductService([personal], [global]);
        var service = new SearchService(fake, new FakeDishService([]));

        var results = await service.SearchGlobalProductsAsync("apple");

        Assert.Single(results);
        Assert.Equal(global.Id, results[0].Product.Id);
        Assert.True(fake.GlobalCalled);
    }

    [Fact]
    public async Task SearchDishesAsync_FindsTypoInDishNameByLevenshtein()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Chicken");
        var dish = new Dish("Chicken Rice Bowl", category.Id);
        dish.Ingredients.Add(new DishIngredient(product.Id, 100m, "g"));

        await using (var db = factory.CreateDbContext())
        {
            db.Categories.Add(category);
            db.Products.Add(product);
            db.Dishes.Add(dish);
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext())
        {
            dish = await db.Dishes
                .Include(d => d.Ingredients)
                .ThenInclude(i => i.Product)
                .SingleAsync(d => d.Id == dish.Id);
        }

        var service = new SearchService(
            new FakeProductService([product], [product]),
            new FakeDishService([dish]));

        var results = await service.SearchDishesAsync("chiken rice bowl");

        Assert.Single(results);
        Assert.Equal(dish.Id, results[0].Dish.Id);
        Assert.False(results[0].Exact);
        Assert.True(results[0].Distance > 0);
    }

    [Fact]
    public async Task SearchDishesAsync_FindsDishByTypoInIngredientName()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Tomato");
        var dish = new Dish("Fresh Salad", category.Id);
        dish.Ingredients.Add(new DishIngredient(product.Id, 100m, "g"));

        await using (var db = factory.CreateDbContext())
        {
            db.Categories.Add(category);
            db.Products.Add(product);
            db.Dishes.Add(dish);
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext())
        {
            dish = await db.Dishes
                .Include(d => d.Ingredients)
                .ThenInclude(i => i.Product)
                .SingleAsync(d => d.Id == dish.Id);
        }

        var service = new SearchService(
            new FakeProductService([product], [product]),
            new FakeDishService([dish]));

        var results = await service.SearchDishesAsync("tomatto");

        Assert.Single(results);
        Assert.Equal(dish.Id, results[0].Dish.Id);
        Assert.False(results[0].Exact);
        Assert.True(results[0].Distance > 0);
    }
}

internal sealed class FakeProductService : IProductService
{
    private readonly IReadOnlyList<Product> _all;
    private readonly IReadOnlyList<Product> _global;
    public bool GlobalCalled { get; private set; }

    public FakeProductService(IReadOnlyList<Product> all, IReadOnlyList<Product> global)
    {
        _all = all;
        _global = global;
    }

    public Task<IReadOnlyList<Product>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        => Task.FromResult(_all);

    public Task<IReadOnlyList<Product>> GetGlobalAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        GlobalCalled = true;
        return Task.FromResult(_global);
    }

    public Task<IReadOnlyList<Product>> GetForUserAsync(Guid userId, bool includeInactive = false, CancellationToken cancellationToken = default)
        => Task.FromResult(_all);

    public Task AddToPersonalCatalogAsync(Guid productId, Guid? userId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveFromPersonalCatalogAsync(Guid productId, Guid? userId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<Product> CreateAsync(string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeDishService : IDishService
{
    private readonly IReadOnlyList<Dish> _dishes;

    public FakeDishService(IReadOnlyList<Dish> dishes)
    {
        _dishes = dishes;
    }

    public Task<IReadOnlyList<Dish>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
        => Task.FromResult(_dishes);

    public Task<Dish> CreateAsync(string name, Guid categoryId, string? description, IEnumerable<DishIngredientInput> ingredients, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, IEnumerable<DishIngredientInput> ingredients, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

