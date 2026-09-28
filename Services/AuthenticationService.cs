using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserService _users;
    private readonly ILoggingService _logging;
    private readonly SessionService _session;
    private readonly ITokenService _tokens;
    private readonly ILocalizationService _loc;

    private static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromDays(7);

    public AuthenticationService(
        IUserService users,
        ILoggingService logging,
        SessionService session,
        ITokenService tokens,
        ILocalizationService loc)
    {
        _users = users;
        _logging = logging;
        _session = session;
        _tokens = tokens;
        _loc = loc;
    }

    public User? CurrentUser => _session.CurrentUser;
    public bool IsAuthenticated => _session.IsAuthenticated;

    public async Task<(bool Success, string Message)> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return (false, _loc.GetString("Auth_EmptyEmailPassword"));
        }

        var user = await _users.FindByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            await _logging.LogActionAsync(null, ActionType.Login, $"Failed login attempt: User not found ({email.Trim()})", cancellationToken: cancellationToken);
            return (false, _loc.GetString("Auth_UserNotRegistered"));
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            await _logging.LogActionAsync(user.Id, ActionType.Login, $"Failed login attempt: Incorrect password ({email.Trim()})", cancellationToken: cancellationToken);
            return (false, _loc.GetString("Auth_IncorrectPassword"));
        }

        await _users.MarkLoginAsync(user.Id, cancellationToken);
        user.MarkLogin();
        _session.SignIn(user);

        await _tokens.SaveSessionAsync(user.Id, DefaultSessionLifetime, cancellationToken);
        
        await _logging.LogActionAsync(user.Id, ActionType.Login, $"User signed in: «{user.DisplayName}».", cancellationToken: cancellationToken);
        return (true, _loc.GetString("Auth_LoginSuccess"));
    }

    public async Task<bool> TryAutoLoginAsync(CancellationToken cancellationToken = default)
    {
        var sessionToken = await _tokens.GetValidSessionAsync(cancellationToken);
        if (sessionToken is null) return false;

        var user = await _users.FindByIdAsync(sessionToken.UserId, cancellationToken);
        if (user is null)
        {
            await _tokens.ClearSessionAsync(cancellationToken);
            return false;
        }

        _session.SignIn(user);
        await _logging.LogActionAsync(user.Id, ActionType.Login, "Auto login via saved token", cancellationToken: cancellationToken);
        return true;
    }

    public async Task<(bool Success, string Message)> RegisterAsync(string email, string displayName, string password, string passwordConfirmation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordConfirmation))
        {
            return (false, _loc.GetString("Auth_FillRequiredFields"));
        }

        if (password != passwordConfirmation)
        {
            return (false, _loc.GetString("Auth_PasswordMismatchErr"));
        }

        if (await _users.EmailExistsAsync(email, cancellationToken))
        {
            return (false, _loc.GetString("Auth_EmailAlreadyRegistered"));
        }

        var result = await _users.RegisterAsync(email, displayName, password, cancellationToken);
        if (result.Success && result.User is { } registeredUser)
        {
            _session.SignIn(registeredUser);
            await _tokens.SaveSessionAsync(registeredUser.Id, DefaultSessionLifetime, cancellationToken);
        }

        return (result.Success, result.Message);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var userId = CurrentUser?.Id;
        await _tokens.ClearSessionAsync(cancellationToken);
        _session.SignOut();

        if (userId.HasValue)
        {
            await _logging.LogActionAsync(userId.Value, ActionType.Logout, "User logged out.", cancellationToken: cancellationToken);
        }
    }

    public void Logout()
    {
        var userId = CurrentUser?.Id;
        _ = _tokens.ClearSessionAsync();
        _session.SignOut();
        _ = _logging.LogActionAsync(userId, ActionType.Logout, "Logout");
    }
}