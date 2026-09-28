using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public sealed class RestrictionService : IRestrictionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILoggingService _logging;

    public RestrictionService(IDbContextFactory<AppDbContext> dbFactory, ILoggingService logging)
    {
        _dbFactory = dbFactory;
        _logging = logging;
    }

    public async Task<IReadOnlyList<DietaryRestriction>> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.DietaryRestrictions.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DietaryRestriction>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserRestrictions.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.DietaryRestriction!).Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task SetUserRestrictionsAsync(Guid userId, IEnumerable<Guid> restrictionIds, CancellationToken cancellationToken = default)
    {
        // Матеріалізуємо колекцію в List одразу, щоб уникнути помилок інтерпретатора EF Core
        var idsList = restrictionIds?.ToList() ?? new List<Guid>();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.UserRestrictions
            .Where(ur => ur.UserId == userId)
            .ToListAsync(cancellationToken);

        // Видаляємо ті, яких немає в новому списку
        var toRemove = existing.Where(ur => !idsList.Contains(ur.DietaryRestrictionId)).ToList();
        db.UserRestrictions.RemoveRange(toRemove);

        // Додаємо нові
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
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Продукт не знайдено.");
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
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var restriction = await db.DietaryRestrictions.OfType<ForbiddenProductRestriction>().SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
        if (restriction is null) return;
        var link = await db.UserRestrictions.SingleOrDefaultAsync(x => x.UserId == userId && x.DietaryRestrictionId == restriction.Id, cancellationToken);
        if (link is not null) db.UserRestrictions.Remove(link);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RestrictionCheckResult> CheckProductAsync(Guid userId, Product product, CancellationToken cancellationToken = default)
    {
        product.RestoreCollectionsFromStorage();
        var restrictions = await GetForUserAsync(userId, cancellationToken);
        foreach (var restriction in restrictions)
        {
            if (!restriction.Allows(product))
                return new RestrictionCheckResult(false, $"Продукт «{product.Name}» не дозволений обмеженням «{restriction.Name}».");
        }
        return new RestrictionCheckResult(true, "Продукт дозволений.");
    }

    public async Task<RestrictionCheckResult> CheckDishAsync(Guid userId, Dish dish, CancellationToken cancellationToken = default)
    {
        var ingredients = dish.Ingredients.ToList();
        if (ingredients.Count == 0) return new RestrictionCheckResult(false, $"Страва «{dish.Name}» не містить продуктів.");
        foreach (var ingredient in ingredients)
        {
            if (ingredient.Product is null) return new RestrictionCheckResult(false, "Для одного з інгредієнтів не завантажено продукт.");
            var result = await CheckProductAsync(userId, ingredient.Product, cancellationToken);
            if (!result.Allowed) return new RestrictionCheckResult(false, $"Страва «{dish.Name}»: {result.Reason}");
        }
        return new RestrictionCheckResult(true, "Страва дозволена.");
    }
}
