using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Tests.Services;

public sealed class StatisticsServiceTests
{
    [Fact]
    public async Task GetAdminStatisticsAsync_ReturnsActualCounts()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var userWithLogin = TestDataFactory.CreateUser(UserRole.User);
        userWithLogin.MarkLogin();
        var userWithoutLogin = new User("second@example.com", "Second", "hash", UserRole.User);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(userWithLogin, userWithoutLogin);
            var category = TestDataFactory.CreateGlobalCategory();
            db.Categories.Add(category);
            db.Products.Add(TestDataFactory.CreateProduct(category.Id, "Food"));
            db.NutritionPlans.AddRange(
                new NutritionPlan(userWithLogin.Id, DateTime.UtcNow.Date, 1, new NutritionTargets(2000m, 150m, 70m, 250m)),
                new NutritionPlan(userWithLogin.Id, DateTime.UtcNow.Date.AddDays(1), 2, new NutritionTargets(2000m, 150m, 70m, 250m)));
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(TestDataFactory.CreateUser(UserRole.Admin));
        var service = new StatisticsService(factory, new StubLocalizationService(), session);
        var stats = await service.GetAdminStatisticsAsync();

        Assert.Equal(2, stats.TotalUsers);
        Assert.Equal(1, stats.UsersWithLogin);
        Assert.Equal(2, stats.TotalPlans);
    }

    [Fact]
    public async Task GetAdminStatisticsAsync_RejectsNonAdminCaller()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        session.SignIn(TestDataFactory.CreateUser(UserRole.User));
        var service = new StatisticsService(factory, new StubLocalizationService(), session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAdminStatisticsAsync());
    }

}
