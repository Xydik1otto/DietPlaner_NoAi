using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public sealed class UserService : IUserService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILoggingService _logging;
    private readonly ILocalizationService _loc;
    private readonly SessionService _session;

    public UserService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILoggingService logging,
        ILocalizationService loc,
        SessionService session)
    {
        _dbFactory = dbFactory;
        _logging = logging;
        _loc = loc;
        _session = session;
    }

    public async Task<(bool Success, string Message, User? User)> RegisterAsync(string email, string displayName, string password, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(x => x.Email == normalizedEmail, cancellationToken))
        {
            return (false, _loc.GetString("User_AlreadyExists"), null);
        }

        var isFirstUser = !await db.Users.AnyAsync(cancellationToken);
        var role = isFirstUser ? UserRole.Admin : UserRole.User;
        var user = new User(normalizedEmail, displayName, BCrypt.Net.BCrypt.HashPassword(password), role);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(user.Id, ActionType.Register, $"Registered account with role {role}", cancellationToken: cancellationToken);

        var msg = isFirstUser ? _loc.GetString("User_RegSuccessAdmin") : _loc.GetString("User_RegSuccess");
        return (true, msg, user);
    }

    public async Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.SingleOrDefaultAsync(x => x.Email == email.Trim().ToLowerInvariant(), cancellationToken);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Users.AsNoTracking().OrderBy(x => x.DisplayName).ToListAsync(cancellationToken);
    }

    public async Task UpdateProfileAsync(User user, CancellationToken cancellationToken = default)
    {
        EnsureCanManageUser(user.Id);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var stored = await db.Users.SingleOrDefaultAsync(x => x.Id == user.Id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_UserNotFound"));

        stored.SetEmail(user.Email.Trim().ToLowerInvariant());
        stored.SetDisplayName(user.DisplayName);
        stored.UpdateNutritionProfile(
            user.BirthDate,
            user.SexForCalculation,
            user.HeightCm,
            user.WeightKg,
            user.ActivityLevel,
            user.Goal,
            user.HealthConditionLabel,
            user.HealthNotes);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_UserNotFound"));
        user.MarkLogin();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        EnsureCanManageUser(userId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_UserNotFound"));

        // Personal catalog entities must be removed before the user itself because
        // Category -> Dish uses Restrict to protect catalog data from accidental deletes.
        var personalDishes = await db.Dishes
            .Where(x => x.OwnerUserId == userId)
            .Include(x => x.Ingredients)
            .ToListAsync(cancellationToken);
        if (personalDishes.Count > 0)
            db.Dishes.RemoveRange(personalDishes);

        var personalCategories = await db.Categories
            .Where(x => x.OwnerUserId == userId)
            .ToListAsync(cancellationToken);
        if (personalCategories.Count > 0)
            db.Categories.RemoveRange(personalCategories);

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(userId, ActionType.Delete, "Deleted user profile", "User", userId, cancellationToken: cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await db.Users.AnyAsync(x => x.Email == normalizedEmail, cancellationToken);
    }

    public async Task ChangeRoleAsync(Guid userId, UserRole newRole, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_UserNotFound"));

        user.SetRole(newRole);
        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            _session.CurrentUser!.Id,
            ActionType.Update,
            $"Changed user role to {newRole}",
            "User",
            userId,
            cancellationToken: cancellationToken);
    }


    private void EnsureCanManageUser(Guid userId)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser is null)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));

        if (_session.CurrentUser.Role != UserRole.Admin && _session.CurrentUser.Id != userId)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }

    private void EnsureAdmin()
    {
        if (!_session.IsAuthenticated || _session.CurrentUser?.Role != UserRole.Admin)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }
}