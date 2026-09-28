using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface ITokenService
{
    Task SaveSessionAsync(Guid userId, TimeSpan lifetime, CancellationToken cancellationToken = default);
    Task<UserSessionToken?> GetValidSessionAsync(CancellationToken cancellationToken = default);
    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}