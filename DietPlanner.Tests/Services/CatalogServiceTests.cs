using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Tests.Services;

public sealed class CatalogServiceTests
{
    [Fact]
    public async Task UserCreatedProduct_IsGlobalAndAddedToPersonalCatalog()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);
        session.SignIn(user);

        Guid categoryId;
        await using (var db = factory.CreateDbContext())
        {
            var category = TestDataFactory.CreateGlobalCategory();
            categoryId = category.Id;
            db.Users.Add(user);
            db.Categories.Add(category);
            await db.SaveChangesAsync();
        }

        var service = new ProductService(factory, session, logging, loc);
        var product = await service.CreateAsync("My Product", categoryId, null, NutritionBasis.Per100Grams, 100m, 100m, 10m, 5m, 10m, [], []);

        await using var verify = factory.CreateDbContext();
        Assert.True(verify.Products.Single(p => p.Id == product.Id).IsGlobal);
        Assert.True(verify.UserCatalogProducts.Any(x => x.UserId == user.Id && x.ProductId == product.Id));
    }

    [Fact]
    public async Task UserDelete_RemovesOnlyPersonalLinkAndKeepsGlobalProduct()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);
        session.SignIn(user);

        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Global Food");

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, product.Id));
            await db.SaveChangesAsync();
        }

        var service = new ProductService(factory, session, logging, loc);
        await service.DeleteAsync(product.Id);

        await using var verify = factory.CreateDbContext();
        Assert.True(await verify.Products.AnyAsync(p => p.Id == product.Id && p.IsActive));
        Assert.False(await verify.UserCatalogProducts.AnyAsync(x => x.UserId == user.Id && x.ProductId == product.Id));
    }

    [Fact]
    public async Task UserCreateCategory_IsPersonalAndUserCanDeleteIt()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);
        session.SignIn(user);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = new CategoryService(factory, session, logging, loc);
        var category = await service.CreateAsync("My Category", "Personal");

        Assert.Equal(user.Id, category.OwnerUserId);
        Assert.False(category.IsGlobal);

        await service.DeleteAsync(category.Id);

        await using var verify = factory.CreateDbContext();
        Assert.False(await verify.Categories.Where(c => c.Id == category.Id && c.IsActive).AnyAsync());
    }

    [Fact]
    public async Task UserCreateDish_IsPersonalAndVisibleOnlyToOwner()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var owner = TestDataFactory.CreateUser(UserRole.User);
        var other = new User("other@example.com", "Other", "hash", UserRole.User);
        session.SignIn(owner);

        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Chicken");

        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(owner, other);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var service = new DishService(factory, session, logging, loc);
        var dish = await service.CreateAsync("My Dish", category.Id, null, [new DishIngredientInput(product.Id, 100m, "g")]);

        Assert.Equal(owner.Id, dish.OwnerUserId);
        Assert.False(dish.IsGlobal);
        Assert.True((await service.GetAllAsync()).Any(x => x.Id == dish.Id));

        session.SignIn(other);
        Assert.False((await service.GetAllAsync()).Any(x => x.Id == dish.Id));
    }

    [Fact]
    public async Task UserCannotUpdateGlobalProduct()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);
        session.SignIn(user);

        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Global Food");

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var service = new ProductService(factory, session, logging, loc);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(
            product.Id,
            "Changed",
            category.Id,
            null,
            NutritionBasis.Per100Grams,
            100m,
            100m,
            10m,
            5m,
            10m,
            [],
            []));
    }
    [Fact]
    public async Task UserCannotUsePersonalCategoryForGlobalProduct()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);
        session.SignIn(user);

        var personalCategory = new Category("My Products", ownerUserId: user.Id);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(personalCategory);
            await db.SaveChangesAsync();
        }

        var service = new ProductService(factory, session, logging, loc);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            "Personal Product", personalCategory.Id, null, NutritionBasis.Per100Grams,
            100m, 100m, 10m, 5m, 10m, [], []));
    }

    [Fact]
    public async Task UserCanCreatePersonalDishUsingPersonalCategoryAndPersonalProducts()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);
        session.SignIn(user);

        var globalCategory = TestDataFactory.CreateGlobalCategory();
        var personalCategory = new Category("My Meals", ownerUserId: user.Id);
        var product = TestDataFactory.CreateProduct(globalCategory.Id, "Chicken");

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.AddRange(globalCategory, personalCategory);
            db.Products.Add(product);
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, product.Id));
            await db.SaveChangesAsync();
        }

        var service = new DishService(factory, session, logging, loc);
        var dish = await service.CreateAsync(
            "My Meal", personalCategory.Id, null,
            [new DishIngredientInput(product.Id, 100m, "g")]);

        Assert.Equal(user.Id, dish.OwnerUserId);
        Assert.Equal(personalCategory.Id, dish.CategoryId);
    }

    [Fact]
    public async Task PersonalProductCatalogContainsOnlyLinkedProducts()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);
        session.SignIn(user);

        var category = TestDataFactory.CreateGlobalCategory();
        var linked = TestDataFactory.CreateProduct(category.Id, "Linked");
        var unlinked = TestDataFactory.CreateProduct(category.Id, "Unlinked");

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.AddRange(linked, unlinked);
            db.UserCatalogProducts.Add(new UserCatalogProduct(user.Id, linked.Id));
            await db.SaveChangesAsync();
        }

        var service = new ProductService(factory, session, logging, loc);
        var products = await service.GetAllAsync();

        Assert.Single(products);
        Assert.Equal(linked.Id, products[0].Id);
    }

    [Fact]
    public async Task UserCannotCreatePersonalDishUsingProductOutsidePersonalCatalog()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var logging = new NoOpLoggingService();
        var loc = new StubLocalizationService();
        var user = TestDataFactory.CreateUser(UserRole.User);
        session.SignIn(user);

        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Global Only");

        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(user);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        var service = new DishService(factory, session, logging, loc);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            "Invalid Personal Dish", category.Id, null,
            [new DishIngredientInput(product.Id, 100m, "g")]));
    }

}
