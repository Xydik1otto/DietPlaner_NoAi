using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class MealIntakeValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task AddIntakeItem_RejectsNonPositiveAmount(int amount)
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory, user);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.AddIntakeItemAsync(user.Id, product.Id, null, amount));
    }

    [Fact]
    public async Task AddIntakeItem_RejectsWhenNeitherProductNorDishSpecified()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory, user);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddIntakeItemAsync(user.Id, null, null, 100m));
    }

    [Fact]
    public async Task AddIntakeItem_RejectsWhenBothProductAndDishSpecified()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id);
        var dish = new Dish("Dish", category.Id);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            db.Dishes.Add(dish);
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory, user);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddIntakeItemAsync(user.Id, product.Id, dish.Id, 100m));
    }

    [Fact]
    public async Task AddIntakeItem_RejectsUnknownProduct()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory, user);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AddIntakeItemAsync(user.Id, Guid.NewGuid(), null, 100m));
    }

    [Fact]
    public async Task AddIntakeItem_RejectsUnknownDish()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory, user);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AddIntakeItemAsync(user.Id, null, Guid.NewGuid(), 100m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task UpdateIntakeItemAmount_RejectsNonPositiveAmount(int amount)
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory, user);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.UpdateIntakeItemAmountAsync(Guid.NewGuid(), amount, user.Id));
    }

    [Fact]
    public async Task AddIntakeItem_RejectsInactiveProduct()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id);
        product.Deactivate();

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory, user);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AddIntakeItemAsync(user.Id, product.Id, null, 100m));
    }

    [Fact]
    public async Task AddIntakeItem_RejectsInactiveDish()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id);
        var dish = new Dish("Dish", category.Id);
        dish.Ingredients.Add(new DishIngredient(product.Id, 100m, "g"));
        typeof(DishIngredient).GetProperty(nameof(DishIngredient.Product))!.SetValue(dish.Ingredients.Single(), product);
        dish.Deactivate();

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            db.Dishes.Add(dish);
            await db.SaveChangesAsync();
        }

        var service = CreateService(factory, user);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AddIntakeItemAsync(user.Id, null, dish.Id, 100m));
    }

    private static MealIntakeService CreateService(
        InMemoryDbContextFactory factory,
        User user)
    {
        var session = new SessionService();
        session.SignIn(user);

        return new MealIntakeService(
            factory,
            new FixedNutritionCalculator(),
            new AllowAllRestrictionService(),
            new NoOpLoggingService(),
            new StubLocalizationService(),
            session);
    }
}
