using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class PlanServiceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task GenerateAsync_CreatesExactlyRequestedNumberOfMeals(int requestedMealCount)
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser();

        await using (var db = factory.CreateDbContext())
        {
            var category = TestDataFactory.CreateGlobalCategory();
            db.Users.Add(user);
            db.Categories.Add(category);
            var chicken = TestDataFactory.CreateProduct(category.Id, "Chicken", 200m, 20m, 5m, 0m);
            db.Products.Add(chicken);
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, chicken.Id));
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new PlanService(
            factory,
            new FixedNutritionCalculator(2000m),
            new AllowAllRestrictionService(),
            logging,
            loc,
            session);

        var result = await service.GenerateAsync(user.Id, requestedMealCount, 2000m);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Plan);
        Assert.Equal(requestedMealCount, result.Plan!.MealCount);

        var meals = result.Plan.Items
            .Select(x => x.MealName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(requestedMealCount, meals.Count);
        Assert.All(result.Plan.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.MealName)));
    }

    [Fact]
    public async Task GenerateWeekAsync_CreatesSevenResultsWithRequestedMealCount()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser();

        await using (var db = factory.CreateDbContext())
        {
            var category = TestDataFactory.CreateGlobalCategory();
            db.Users.Add(user);
            db.Categories.Add(category);
            var chicken = TestDataFactory.CreateProduct(category.Id, "Chicken", 200m, 20m, 5m, 0m);
            db.Products.Add(chicken);
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, chicken.Id));
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new PlanService(
            factory,
            new FixedNutritionCalculator(2000m),
            new AllowAllRestrictionService(),
            logging,
            loc,
            session);

        var results = await service.GenerateWeekAsync(user.Id, 8, 2000m);

        Assert.Equal(7, results.Count);
        Assert.All(results, result =>
        {
            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Plan);
            Assert.Equal(8, result.Plan!.MealCount);
            Assert.Equal(8, result.Plan.Items.Select(i => i.MealName).Distinct().Count());
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(100)]
    public async Task GenerateAsync_ClampsOutOfRangeMealCountToValidRange(int invalidMealCount)
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser();

        await using (var db = factory.CreateDbContext())
        {
            var category = TestDataFactory.CreateGlobalCategory();
            db.Users.Add(user);
            db.Categories.Add(category);
            var chicken = TestDataFactory.CreateProduct(category.Id, "Chicken", 200m, 20m, 5m, 0m);
            db.Products.Add(chicken);
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, chicken.Id));
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new PlanService(
            factory,
            new FixedNutritionCalculator(2000m),
            new AllowAllRestrictionService(),
            logging,
            loc,
            session);

        var result = await service.GenerateAsync(user.Id, invalidMealCount, 2000m);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Plan);
        var expected = invalidMealCount < 1 ? 1 : 8;
        Assert.Equal(expected, result.Plan!.MealCount);
    }

    [Fact]
    public async Task ReplacePlanItemAsync_RejectsPlanItemOwnedByAnotherUser()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var owner = TestDataFactory.CreateUser(UserRole.User);
        var attacker = new User("attacker@example.com", "Attacker", "hash", UserRole.User);

        Guid planItemId;
        Guid attackerProductId;
        await using (var db = factory.CreateDbContext())
        {
            var category = TestDataFactory.CreateGlobalCategory();
            var product = TestDataFactory.CreateProduct(category.Id, "Original", 100m);
            var replacement = TestDataFactory.CreateProduct(category.Id, "Replacement", 200m);
            db.Users.AddRange(owner, attacker);
            db.Categories.Add(category);
            db.Products.AddRange(product, replacement);

            var plan = new NutritionPlan(owner.Id, DateTime.UtcNow.Date, 1, new NutritionTargets(2000m, 150m, 70m, 250m));
            var item = new PlanItem(MealType.Breakfast, "Breakfast", product.Id, null, 100m, "g", new NutritionSnapshot(100m, 10m, 5m, 10m));
            plan.Items.Add(item);
            db.NutritionPlans.Add(plan);
            await db.SaveChangesAsync();
            planItemId = item.Id;
            attackerProductId = replacement.Id;
        }

        var session = new SessionService();
        session.SignIn(attacker);
        var service = new PlanService(factory, new FixedNutritionCalculator(), new AllowAllRestrictionService(), logging, loc, session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReplacePlanItemAsync(planItemId, attackerProductId, null, 100m));

        await using var verify = factory.CreateDbContext();
        var storedItem = await verify.PlanItems.SingleAsync(x => x.Id == planItemId);
        Assert.NotEqual(attackerProductId, storedItem.ProductId);
    }

    [Fact]
    public async Task GenerateAsync_UserUsesOnlyProductsFromPersonalCatalog()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);

        Guid linkedId;
        Guid unlinkedId;
        await using (var db = factory.CreateDbContext())
        {
            var category = TestDataFactory.CreateGlobalCategory();
            db.Users.Add(user);
            db.Categories.Add(category);
            var linked = TestDataFactory.CreateProduct(category.Id, "Personal Chicken", 200m, 20m, 5m, 0m);
            var unlinked = TestDataFactory.CreateProduct(category.Id, "Global Only Chicken", 200m, 20m, 5m, 0m);
            linkedId = linked.Id;
            unlinkedId = unlinked.Id;
            db.Products.AddRange(linked, unlinked);
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, linked.Id));
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new PlanService(
            factory,
            new FixedNutritionCalculator(2000m),
            new AllowAllRestrictionService(),
            logging,
            loc,
            session);

        var result = await service.GenerateAsync(user.Id, 1, 2000m);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Plan);
        Assert.NotEmpty(result.Plan!.Items);
        Assert.All(result.Plan.Items, item =>
        {
            Assert.Equal(linkedId, item.ProductId);
            Assert.NotEqual(unlinkedId, item.ProductId);
        });
    }

}
