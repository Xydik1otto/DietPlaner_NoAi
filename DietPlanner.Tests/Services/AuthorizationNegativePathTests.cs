using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class AuthorizationNegativePathTests
{
    [Fact]
    public void AuthorizationService_UserCannotManageGlobalCatalog()
    {
        var session = new SessionService();
        session.SignIn(TestDataFactory.CreateUser(UserRole.User));
        var service = new AuthorizationService(session);

        Assert.False(service.CanManageGlobalCatalog);
        Assert.False(service.CanViewAdminArea);
        Assert.False(service.CanViewAllUsers);
    }

    [Fact]
    public void AuthorizationService_AdminCanManageGlobalCatalog()
    {
        var session = new SessionService();
        session.SignIn(TestDataFactory.CreateUser(UserRole.Admin));
        var service = new AuthorizationService(session);

        Assert.True(service.CanManageGlobalCatalog);
        Assert.True(service.CanViewAdminArea);
        Assert.True(service.CanViewAllUsers);
    }

    [Fact]
    public async Task ProductService_UserCannotUpdateGlobalProduct()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var user = TestDataFactory.CreateUser(UserRole.User);
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(user);
        var service = new ProductService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(
            product.Id, "Changed", category.Id, null, NutritionBasis.Per100Grams,
            100m, 100m, 10m, 5m, 10m, [], []));
    }

    [Fact]
    public async Task ProductService_UserCannotManageAnotherUsersPersonalCatalog()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var currentUser = TestDataFactory.CreateUser(UserRole.User);
        var otherUser = new User("other2@example.com", "Other", "hash", UserRole.User);
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(currentUser, otherUser);
            db.Categories.Add(category);
            db.Products.Add(product);
            db.UserCatalogProducts.Add(new UserCatalogProduct(otherUser.Id, product.Id));
            await db.SaveChangesAsync();
        }

        var session = new SessionService();
        session.SignIn(currentUser);
        var service = new ProductService(factory, session, new NoOpLoggingService(), new StubLocalizationService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RemoveFromPersonalCatalogAsync(product.Id, otherUser.Id));
    }
}
