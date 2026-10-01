using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Tests.Services;

public sealed class ModelAndServiceInputGuardTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Product_CalculateNutritionSnapshot_RejectsNonPositiveAmount(decimal amount)
    {
        var product = TestDataFactory.CreateProduct(TestDataFactory.CreateGlobalCategory().Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => product.CalculateNutritionSnapshot(amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    public void NutritionCalculator_CalculateDishPortion_RejectsNonPositiveMultiplier(decimal multiplier)
    {
        var calculator = new NutritionCalculator(new StubLocalizationService());
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id);
        var dish = new Dish("Dish", category.Id);
        var ingredient = new DishIngredient(product.Id, 100m, "g");
        typeof(DishIngredient).GetProperty(nameof(DishIngredient.Product))!.SetValue(ingredient, product);
        dish.Ingredients.Add(ingredient);

        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.CalculateDishPortion(dish, multiplier));
    }

    [Fact]
    public void Dish_CalculateNutrition_RejectsMissingLoadedProduct()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var dish = new Dish("Dish", category.Id);
        dish.Ingredients.Add(new DishIngredient(Guid.NewGuid(), 100m, "g"));

        Assert.Throws<InvalidOperationException>(() => dish.CalculateNutrition());
    }

    [Theory]
    [InlineData(-1, 100, 10, 5, 10)]
    [InlineData(100, -1, 10, 5, 10)]
    [InlineData(100, 100, -1, 5, 10)]
    [InlineData(100, 100, 10, -1, 10)]
    [InlineData(100, 100, 10, 5, -1)]
    public async Task ProductService_Create_RejectsNegativeNutritionBeforeDatabaseWrite(
        decimal referenceAmount,
        decimal calories,
        decimal protein,
        decimal fat,
        decimal carbs)
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var user = TestDataFactory.CreateUser();
        session.SignIn(user);
        var service = new ProductService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync(
            "Bad Product", Guid.NewGuid(), null, NutritionBasis.Per100Grams,
            referenceAmount, calories, protein, fat, carbs, [], []));
    }

    [Fact]
    public async Task ProductService_Create_RejectsZeroReferenceAmount()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        session.SignIn(TestDataFactory.CreateUser());
        var service = new ProductService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync(
            "Bad Product", Guid.NewGuid(), null, NutritionBasis.Per100Grams,
            0m, 100m, 10m, 5m, 10m, [], []));
    }

    [Fact]
    public async Task ProductService_Create_RequiresAuthentication()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var service = new ProductService(factory, new SessionService(), new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            "Product", Guid.NewGuid(), null, NutritionBasis.Per100Grams,
            100m, 100m, 10m, 5m, 10m, [], []));
    }

    [Fact]
    public async Task CategoryService_Create_RejectsDuplicateActiveNamePerOwner()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var user = TestDataFactory.CreateUser();

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        session.SignIn(user);
        var service = new CategoryService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await service.CreateAsync("Breakfast", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("  breakfast  ", "duplicate"));
    }

    [Fact]
    public async Task CategoryService_Update_RejectsEditingAnotherUsersCategory()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var owner = TestDataFactory.CreateUser();
        var otherUser = new User("other@example.com", "Other", "hash", UserRole.User);
        var category = new Category("Private", null, owner.Id);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(owner, otherUser);
            db.Categories.Add(category);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(otherUser);
        var service = new CategoryService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(category.Id, "Hacked", null));
    }

    [Fact]
    public async Task DishService_Create_RejectsEmptyIngredientCollection()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new DishService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("Dish", category.Id, null, []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task DishService_Create_RejectsNonPositiveIngredientAmount(decimal amount)
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
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, product.Id));
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new DishService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync(
            "Dish", category.Id, null, [new DishIngredientInput(product.Id, amount, "g")]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task DishService_Create_RejectsEmptyIngredientUnit(string? unit)
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
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, product.Id));
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new DishService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            "Dish", category.Id, null, [new DishIngredientInput(product.Id, 100m, unit!)]));
    }

    [Fact]
    public async Task DishService_Create_RejectsEmptyProductId()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new DishService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            "Dish", category.Id, null, [new DishIngredientInput(Guid.Empty, 100m, "g")]));
    }
}
