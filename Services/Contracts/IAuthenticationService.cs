using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface IAuthenticationService
{
    User? CurrentUser { get; }
    bool IsAuthenticated { get; }

    Task<(bool Success, string Message)> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<bool> TryAutoLoginAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> RegisterAsync(string email, string displayName, string password, string passwordConfirmation, CancellationToken cancellationToken = default);
    void Logout();
}
