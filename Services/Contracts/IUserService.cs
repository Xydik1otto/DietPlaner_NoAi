using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface IUserService
{
    Task<(bool Success, string Message, User? User)> RegisterAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken = default);

    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(User user, CancellationToken cancellationToken = default);
    Task MarkLoginAsync(Guid userId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
    Task ChangeRoleAsync(Guid userId, UserRole newRole, CancellationToken cancellationToken = default);
}