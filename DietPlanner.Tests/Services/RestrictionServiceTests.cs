using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class RestrictionServiceTests
{
    [Fact]
    public async Task ForbiddenProductRestriction_BlocksThenAllowsProductAfterRemoval()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var user = TestDataFactory.CreateUser();
        session.SignIn(user);
        var service = new RestrictionService(factory, new NoOpLoggingService(), new StubLocalizationService(), session);
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Milk");

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        await service.AddForbiddenProductAsync(user.Id, product.Id);
        var blocked = await service.CheckProductAsync(user.Id, product);

        Assert.False(blocked.Allowed);

        await service.RemoveForbiddenProductAsync(user.Id, product.Id);
        var allowed = await service.CheckProductAsync(user.Id, product);

        Assert.True(allowed.Allowed);
    }

    [Fact]
    public async Task AllergenRestriction_BlocksProductContainingAllergen()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var user = TestDataFactory.CreateUser();
        session.SignIn(user);
        var service = new RestrictionService(factory, new NoOpLoggingService(), new StubLocalizationService(), session);
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Yogurt");
        product.SetAllergens(["milk"]);
        var restriction = new AllergenRestriction("milk");

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            db.DietaryRestrictions.Add(restriction);
            db.UserRestrictions.Add(new UserRestriction(user.Id, restriction.Id));
            await db.SaveChangesAsync();
        }

        var result = await service.CheckProductAsync(user.Id, product);

        Assert.False(result.Allowed);
    }
}
