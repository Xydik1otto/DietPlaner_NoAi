using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Tests.Services;

public sealed class OwnershipSecurityTests
{
    [Fact]
    public async Task UserCannotUpdateAnotherUsersProfile()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var owner = TestDataFactory.CreateUser();
        var other = new User("other@example.com", "Other", "hash", UserRole.User);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(owner, other);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(owner);
        var service = new UserService(factory, new NoOpLoggingService(), new StubLocalizationService(), session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateProfileAsync(other));
    }

    [Fact]
    public async Task UserCannotDeleteAnotherUser()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var owner = TestDataFactory.CreateUser();
        var other = new User("other@example.com", "Other", "hash", UserRole.User);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(owner, other);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(owner);
        var service = new UserService(factory, new NoOpLoggingService(), new StubLocalizationService(), session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DeleteAsync(other.Id));

        await using var verify = factory.CreateDbContext();
        Assert.NotNull(await verify.Users.SingleOrDefaultAsync(x => x.Id == other.Id));
    }

    [Fact]
    public async Task UserCannotReadAnotherUsersStatistics()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var owner = TestDataFactory.CreateUser();
        var other = new User("other@example.com", "Other", "hash", UserRole.User);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(owner, other);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(owner);
        var service = new StatisticsService(factory, new StubLocalizationService(), session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetDailyStatsAsync(other.Id, DateTime.UtcNow.Date));
    }

    [Fact]
    public async Task ReplacePlanItemRejectsZeroPortionAndNoReplacement()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser();
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id);
        Guid itemId;
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            var plan = new NutritionPlan(user.Id, DateTime.UtcNow.Date, 1, new NutritionTargets(2000m, 150m, 70m, 250m));
            var item = new PlanItem(MealType.Breakfast, "Breakfast", product.Id, null, 100m, "g", new NutritionSnapshot(165m, 31m, 3.6m, 0m));
            plan.Items.Add(item);
            db.NutritionPlans.Add(plan);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new PlanService(factory, new FixedNutritionCalculator(), new AllowAllRestrictionService(), new NoOpLoggingService(), new StubLocalizationService(), session);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReplacePlanItemAsync(itemId, product.Id, null, 0m));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplacePlanItemAsync(itemId, null, null, 100m));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReplacePlanItemAsync(itemId, product.Id, product.Id, 100m));
    }
}
