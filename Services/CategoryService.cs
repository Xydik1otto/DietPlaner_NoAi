using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public sealed class CategoryService : ICategoryService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SessionService _session;
    private readonly ILoggingService _logging;
    private readonly ILocalizationService _loc;

    public CategoryService(
        IDbContextFactory<AppDbContext> dbFactory,
        SessionService session,
        ILoggingService logging,
        ILocalizationService loc)
    {
        _dbFactory = dbFactory;
        _session = session;
        _logging = logging;
        _loc = loc;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Categories.AsNoTracking().AsQueryable();

        if (_session.CurrentUser?.Role == UserRole.User)
            query = query.Where(x => x.OwnerUserId == null || x.OwnerUserId == _session.CurrentUser.Id);
        else
            query = query.Where(x => x.OwnerUserId == null);

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> GetGlobalAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Categories
            .AsNoTracking()
            .Where(x => x.OwnerUserId == null)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<Category> CreateAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var currentUser = _session.CurrentUser!;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = name.Trim();
        var ownerUserId = currentUser.Role == UserRole.Admin ? (Guid?)null : currentUser.Id;

        var exists = await db.Categories.AnyAsync(
            x => x.IsActive &&
                 x.OwnerUserId == ownerUserId &&
                 x.Name.ToLower() == normalized.ToLower(),
            cancellationToken);
        if (exists)
            throw new InvalidOperationException(_loc.GetString("Err_CategoryExists"));

        var category = new Category(normalized, description, ownerUserId);
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            currentUser.Id,
            ActionType.Create,
            currentUser.Role == UserRole.Admin ? "Created global category" : "Created personal category",
            "Category",
            category.Id,
            cancellationToken: cancellationToken);

        return category;
    }

    public async Task UpdateAsync(
        Guid id,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var currentUser = _session.CurrentUser!;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));

        if (currentUser.Role == UserRole.Admin)
        {
            if (!category.IsGlobal)
                throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }
        else if (category.OwnerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }

        var normalized = name.Trim();
        var duplicate = await db.Categories.AnyAsync(
            x => x.Id != id && x.IsActive && x.OwnerUserId == category.OwnerUserId && x.Name.ToLower() == normalized.ToLower(),
            cancellationToken);
        if (duplicate)
            throw new InvalidOperationException(_loc.GetString("Err_CategoryExists"));

        category.SetName(normalized);
        category.SetDescription(description);
        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(currentUser.Id, ActionType.Update, "Updated category", "Category", id, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var currentUser = _session.CurrentUser!;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));

        if (currentUser.Role == UserRole.Admin)
        {
            if (!category.IsGlobal)
                throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }
        else if (category.OwnerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }

        category.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(currentUser.Id, ActionType.Delete, "Deactivated category", "Category", id, cancellationToken: cancellationToken);
    }

    private void EnsureAuthenticated()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }
}
