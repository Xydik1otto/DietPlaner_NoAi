using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public async Task LoginAsync_RejectsEmptyCredentials()
    {
        var session = new SessionService();
        var service = new AuthenticationService(
            new FakeUserService(),
            new NoOpLoggingService(),
            session,
            new FakeTokenService(),
            new StubLocalizationService());

        var result = await service.LoginAsync("", "");

        Assert.False(result.Success);
        Assert.Equal("Email and password are required", result.Message);
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task LoginAsync_AuthenticatesCorrectPassword()
    {
        var user = new User("user@example.com", "User", BCrypt.Net.BCrypt.HashPassword("correct-password"), UserRole.User);
        var session = new SessionService();
        var service = new AuthenticationService(
            new FakeUserService(user),
            new NoOpLoggingService(),
            session,
            new FakeTokenService(),
            new StubLocalizationService());

        var result = await service.LoginAsync("user@example.com", "correct-password");

        Assert.True(result.Success, result.Message);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(user.Id, session.CurrentUser!.Id);
    }

    [Fact]
    public async Task LoginAsync_RejectsWrongPassword()
    {
        var user = new User("user@example.com", "User", BCrypt.Net.BCrypt.HashPassword("correct-password"), UserRole.User);
        var session = new SessionService();
        var service = new AuthenticationService(
            new FakeUserService(user),
            new NoOpLoggingService(),
            session,
            new FakeTokenService(),
            new StubLocalizationService());

        var result = await service.LoginAsync("user@example.com", "wrong-password");

        Assert.False(result.Success);
        Assert.False(session.IsAuthenticated);
    }

    [Fact]
    public async Task RegisterAsync_RejectsPasswordMismatch()
    {
        var service = new AuthenticationService(
            new FakeUserService(),
            new NoOpLoggingService(),
            new SessionService(),
            new FakeTokenService(),
            new StubLocalizationService());

        var result = await service.RegisterAsync("user@example.com", "User", "password1", "password2");

        Assert.False(result.Success);
        Assert.Equal("Passwords do not match", result.Message);
    }

    [Fact]
    public async Task LogoutAsync_ClearsSession()
    {
        var session = new SessionService();
        var user = new User("user@example.com", "User", "hash", UserRole.User);
        session.SignIn(user);
        var tokens = new FakeTokenService();
        var service = new AuthenticationService(
            new FakeUserService(user),
            new NoOpLoggingService(),
            session,
            tokens,
            new StubLocalizationService());

        await service.LogoutAsync();

        Assert.False(session.IsAuthenticated);
        Assert.True(tokens.Cleared);
    }
}

internal sealed class FakeUserService : IUserService
{
    private readonly User? _user;

    public FakeUserService(User? user = null) => _user = user;

    public Task<(bool Success, string Message, User? User)> RegisterAsync(string email, string displayName, string password, CancellationToken cancellationToken = default)
        => Task.FromResult((true, "registered", new User(email, displayName, BCrypt.Net.BCrypt.HashPassword(password), UserRole.User)));

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(_user is not null && _user.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase) ? _user : null);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_user?.Id == id ? _user : null);
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_user?.Id == id ? _user : null);
    public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<User>>(_user is null ? [] : [_user]);
    public Task UpdateProfileAsync(User user, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task MarkLoginAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task ChangeRoleAsync(Guid userId, UserRole newRole, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeTokenService : ITokenService
{
    public bool Cleared { get; private set; }
    public UserSessionToken? Saved { get; private set; }

    public Task SaveSessionAsync(Guid userId, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        Saved = new UserSessionToken
        {
            UserId = userId,
            Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = DateTime.UtcNow.Add(lifetime)
        };
        return Task.CompletedTask;
    }

    public Task<UserSessionToken?> GetValidSessionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Saved is not null && Saved.ExpiresAtUtc > DateTime.UtcNow ? Saved : null);

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        Cleared = true;
        Saved = null;
        return Task.CompletedTask;
    }
}
