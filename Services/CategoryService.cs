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

    public CategoryService(IDbContextFactory<AppDbContext> dbFactory, SessionService session, ILoggingService logging, ILocalizationService loc)
    {
        _dbFactory = dbFactory;
        _session = session;
        _logging = logging;
        _loc = loc;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Categories.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<Category> CreateAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        EnsureCanEditCatalog();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var normalized = name.Trim();
        if (await db.Categories.AnyAsync(x => x.Name.ToLower() == normalized.ToLower(), cancellationToken))
            throw new InvalidOperationException(_loc.GetString("Err_CategoryExists"));

        var category = new Category(normalized, description);
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Create, "Created category", "Category", category.Id, cancellationToken: cancellationToken);
        return category;
    }

    public async Task UpdateAsync(Guid id, string name, string? description, CancellationToken cancellationToken = default)
    {
        EnsureCanEditCatalog();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));
        category.SetName(name);
        category.SetDescription(description);
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Update, "Updated category", "Category", id, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureCanEditCatalog();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));
        category.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Delete, "Deactivated category", "Category", id, cancellationToken: cancellationToken);
    }

    private void EnsureCanEditCatalog()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }
}