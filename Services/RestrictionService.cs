using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public sealed class RestrictionService : IRestrictionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILoggingService _logging;
    private readonly ILocalizationService _loc;
    private readonly SessionService _session;

    public RestrictionService(IDbContextFactory<AppDbContext> dbFactory, ILoggingService logging, ILocalizationService loc, SessionService session)
    {
        _dbFactory = dbFactory;
        _logging = logging;
        _loc = loc;
        _session = session;
    }

    public async Task<IReadOnlyList<DietaryRestriction>> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DietaryRestrictions.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DietaryRestriction>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        EnsureSameUser(userId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserRestrictions.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.DietaryRestriction!).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task SetUserRestrictionsAsync(Guid userId, IEnumerable<Guid> restrictionIds, CancellationToken cancellationToken = default)
    {
        EnsureSameUser(userId);
        var idsList = restrictionIds?.ToList() ?? new List<Guid>();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.UserRestrictions
            .Where(ur => ur.UserId == userId)
            .ToListAsync(cancellationToken);

        var toRemove = existing.Where(ur => !idsList.Contains(ur.DietaryRestrictionId)).ToList();
        db.UserRestrictions.RemoveRange(toRemove);

        var existingIds = existing.Select(ur => ur.DietaryRestrictionId).ToHashSet();
        foreach (var id in idsList)
        {
            if (!existingIds.Contains(id))
            {
                db.UserRestrictions.Add(new UserRestriction(userId, id));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddForbiddenProductAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default)
    {
        EnsureSameUser(userId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));
        var restriction = await db.DietaryRestrictions.OfType<ForbiddenProductRestriction>().SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
        if (restriction is null)
        {
            restriction = new ForbiddenProductRestriction(product.Id, product.Name);
            db.DietaryRestrictions.Add(restriction);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.UserRestrictions.AnyAsync(x => x.UserId == userId && x.DietaryRestrictionId == restriction.Id, cancellationToken))
        {
            db.UserRestrictions.Add(new UserRestriction(userId, restriction.Id));
            await db.SaveChangesAsync(cancellationToken);
        }
        await _logging.LogActionAsync(userId, ActionType.Update, $"Forbidden product: {product.Name}", "Product", productId, cancellationToken: cancellationToken);
    }

    public async Task RemoveForbiddenProductAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default)
    {
        EnsureSameUser(userId);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var restriction = await db.DietaryRestrictions.OfType<ForbiddenProductRestriction>().SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
        if (restriction is null) return;
        var link = await db.UserRestrictions.SingleOrDefaultAsync(x => x.UserId == userId && x.DietaryRestrictionId == restriction.Id, cancellationToken);
        if (link is not null) db.UserRestrictions.Remove(link);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RestrictionCheckResult> CheckProductAsync(Guid userId, Product product, CancellationToken cancellationToken = default)
    {
        EnsureSameUser(userId);
        product.RestoreCollectionsFromStorage();
        var restrictions = await GetForUserAsync(userId, cancellationToken);
        foreach (var restriction in restrictions)
        {
            if (!restriction.Allows(product))
                return new RestrictionCheckResult(false, string.Format(_loc.GetString("Restr_ProductNotAllowed"), product.Name, restriction.Name));
        }
        return new RestrictionCheckResult(true, _loc.GetString("Restr_ProductAllowed"));
    }

    public async Task<RestrictionCheckResult> CheckDishAsync(Guid userId, Dish dish, CancellationToken cancellationToken = default)
    {
        EnsureSameUser(userId);
        var ingredients = dish.Ingredients.ToList();
        if (ingredients.Count == 0) return new RestrictionCheckResult(false, string.Format(_loc.GetString("Restr_DishNoProducts"), dish.Name));
        foreach (var ingredient in ingredients)
        {
            if (ingredient.Product is null) return new RestrictionCheckResult(false, _loc.GetString("Restr_IngredientNoProduct"));
            var result = await CheckProductAsync(userId, ingredient.Product, cancellationToken);
            if (!result.Allowed) return new RestrictionCheckResult(false, string.Format(_loc.GetString("Restr_DishReason"), dish.Name, result.Reason));
        }
        return new RestrictionCheckResult(true, _loc.GetString("Restr_DishAllowed"));
    }

    private void EnsureSameUser(Guid userId)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser?.Id != userId)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }
}
