using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class AuthenticationNegativePathTests
{
    [Theory]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData("user@example.com", "")]
    [InlineData("user@example.com", "   ")]
    public async Task LoginAsync_RejectsBlankCredentialValues(string email, string password)
    {
        var session = new SessionService();
        var service = new AuthenticationService(
            new FakeUserService(),
            new NoOpLoggingService(),
            session,
            new FakeTokenService(),
            new StubLocalizationService());

        var result = await service.LoginAsync(email, password);

        Assert.False(result.Success);
        Assert.False(session.IsAuthenticated);
    }

    [Theory]
    [InlineData("", "Name", "password", "password")]
    [InlineData("user@example.com", "", "password", "password")]
    [InlineData("user@example.com", "Name", "", "")]
    [InlineData("user@example.com", "Name", "password", "")]
    public async Task RegisterAsync_RejectsAnyRequiredBlankField(string email, string name, string password, string confirmation)
    {
        var service = new AuthenticationService(
            new FakeUserService(),
            new NoOpLoggingService(),
            new SessionService(),
            new FakeTokenService(),
            new StubLocalizationService());

        var result = await service.RegisterAsync(email, name, password, confirmation);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("password", "PASSWORD")]
    [InlineData("abcdefgh", "abcdefg")]
    [InlineData("correct", "correct ")]
    public async Task RegisterAsync_RejectsPasswordMismatch(string password, string confirmation)
    {
        var service = new AuthenticationService(
            new FakeUserService(),
            new NoOpLoggingService(),
            new SessionService(),
            new FakeTokenService(),
            new StubLocalizationService());

        var result = await service.RegisterAsync("user@example.com", "Name", password, confirmation);

        Assert.False(result.Success);
        Assert.Equal("Passwords do not match", result.Message);
    }

    [Fact]
    public async Task RegisterAsync_RejectsAlreadyRegisteredEmail()
    {
        var service = new AuthenticationService(
            new EmailAlreadyExistsUserService(),
            new NoOpLoggingService(),
            new SessionService(),
            new FakeTokenService(),
            new StubLocalizationService());

        var result = await service.RegisterAsync("user@example.com", "Name", "password123", "password123");

        Assert.False(result.Success);
        Assert.Equal("Email is already registered", result.Message);
    }
}

internal sealed class EmailAlreadyExistsUserService : IUserService
{
    public Task<(bool Success, string Message, User? User)> RegisterAsync(string email, string displayName, string password, CancellationToken cancellationToken = default)
        => Task.FromResult<(bool Success, string Message, User? User)>((false, "not called", null));

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
    public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>([]);
    public Task UpdateProfileAsync(User user, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task MarkLoginAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task ChangeRoleAsync(Guid userId, UserRole newRole, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
