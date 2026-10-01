using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class MealIntakeServiceTests
{
    [Fact]
    public async Task AddUpdateDeleteProductIntake_WorksEndToEnd()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Apple", 100m, 1m, 0.5m, 25m);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new MealIntakeService(
            factory,
            new FixedNutritionCalculator(),
            new AllowAllRestrictionService(),
            new NoOpLoggingService(),
            new StubLocalizationService(),
            session);

        await service.AddIntakeItemAsync(user.Id, product.Id, null, 100m);
        var afterAdd = await service.GetTodayIntakesAsync(user.Id);

        Assert.Single(afterAdd);
        Assert.Equal("Apple", afterAdd[0].ItemName);
        Assert.Equal(100d, afterAdd[0].Calories);
        var itemId = afterAdd[0].Id;

        await service.UpdateIntakeItemAmountAsync(itemId, 200m, user.Id);
        var afterUpdate = await service.GetTodayIntakesAsync(user.Id);

        Assert.Single(afterUpdate);
        Assert.Equal(200d, afterUpdate[0].Calories);
        Assert.Equal(200d, afterUpdate[0].WeightGrams);

        await service.DeleteIntakeItemByIdAsync(itemId, user.Id);
        var afterDelete = await service.GetTodayIntakesAsync(user.Id);

        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task RemoveIntakeItemByFood_RemovesMatchingItem()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Banana", 90m);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new MealIntakeService(
            factory,
            new FixedNutritionCalculator(),
            new AllowAllRestrictionService(),
            new NoOpLoggingService(),
            new StubLocalizationService(),
            session);

        await service.AddIntakeItemAsync(user.Id, product.Id, null, 100m);
        await service.RemoveIntakeItemByFoodAsync(user.Id, product.Id, null, product.Name);

        Assert.Empty(await service.GetTodayIntakesAsync(user.Id));
    }
}
