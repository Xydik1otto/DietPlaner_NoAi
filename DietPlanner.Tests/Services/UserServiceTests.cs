using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class UserServiceTests
{
    [Fact]
    public async Task RegisterAsync_FirstUserBecomesAdmin_SecondUserBecomesUser()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var service = new UserService(factory, new NoOpLoggingService(), new StubLocalizationService(), new SessionService());

        var first = await service.RegisterAsync("first@example.com", "First", "password123");
        var second = await service.RegisterAsync("second@example.com", "Second", "password123");

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(UserRole.Admin, first.User!.Role);
        Assert.Equal(UserRole.User, second.User!.Role);
        Assert.True(BCrypt.Net.BCrypt.Verify("password123", first.User.PasswordHash));
        Assert.True(await service.EmailExistsAsync("FIRST@EXAMPLE.COM"));
    }

    [Fact]
    public async Task RegisterAsync_RejectsDuplicateEmail()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var service = new UserService(factory, new NoOpLoggingService(), new StubLocalizationService(), new SessionService());

        var first = await service.RegisterAsync("same@example.com", "First", "password123");
        var second = await service.RegisterAsync(" SAME@EXAMPLE.COM ", "Second", "password123");

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Null(second.User);
    }

    [Fact]
    public async Task ChangeRoleAsync_RejectsNonAdminCaller()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var admin = TestDataFactory.CreateUser(UserRole.Admin);
        var target = new User("target@example.com", "Target", "hash", UserRole.User);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(admin, target);
            await db.SaveChangesAsync();
        }

        session.SignIn(target);
        var service = new UserService(factory, new NoOpLoggingService(), new StubLocalizationService(), session);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ChangeRoleAsync(admin.Id, UserRole.User));

        await using var verify = factory.CreateDbContext();
        Assert.Equal(UserRole.Admin, (await verify.Users.SingleAsync(x => x.Id == admin.Id)).Role);
    }

    [Fact]
    public async Task ChangeRoleAsync_AllowsAdminCaller()
    {
        await using var factory = await InMemoryDbContextFactory.CreateAsync();
        var session = new SessionService();
        var admin = TestDataFactory.CreateUser(UserRole.Admin);
        var target = new User("target@example.com", "Target", "hash", UserRole.User);

        await using (var db = factory.CreateDbContext())
        {
            db.Users.AddRange(admin, target);
            await db.SaveChangesAsync();
        }

        session.SignIn(admin);
        var service = new UserService(factory, new NoOpLoggingService(), new StubLocalizationService(), session);

        await service.ChangeRoleAsync(target.Id, UserRole.Admin);

        await using var verify = factory.CreateDbContext();
        Assert.Equal(UserRole.Admin, (await verify.Users.SingleAsync(x => x.Id == target.Id)).Role);
    }

}
