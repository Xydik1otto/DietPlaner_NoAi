using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public sealed class DishService : IDishService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SessionService _session;
    private readonly ILoggingService _logging;
    private readonly ILocalizationService _loc;

    public DishService(
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

    public async Task<IReadOnlyList<Dish>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Dishes
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.Ingredients)
                .ThenInclude(x => x.Product)
            .AsQueryable();

        if (_session.CurrentUser?.Role == UserRole.User)
            query = query.Where(x => x.OwnerUserId == _session.CurrentUser.Id);
        else
            query = query.Where(x => x.OwnerUserId == null);

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<Dish> CreateAsync(
        string name,
        Guid categoryId,
        string? description,
        IEnumerable<DishIngredientInput> ingredients,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var currentUser = _session.CurrentUser!;
        var input = ValidateIngredients(ingredients).ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories
            .SingleOrDefaultAsync(x => x.Id == categoryId && x.IsActive, cancellationToken);
        if (category == null)
            throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));

        if (currentUser.Role == UserRole.Admin)
        {
            if (!category.IsGlobal)
                throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }
        else if (!category.IsGlobal && category.OwnerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }

        var productIds = input.Select(x => x.ProductId).Distinct().ToList();
        var count = await db.Products.CountAsync(x => productIds.Contains(x.Id) && x.IsActive && x.IsGlobal, cancellationToken);
        if (count != productIds.Count)
            throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));

        if (currentUser.Role == UserRole.User)
        {
            var personalProductCount = await db.UserCatalogProducts
                .CountAsync(link => link.UserId == currentUser.Id && productIds.Contains(link.ProductId), cancellationToken);
            if (personalProductCount != productIds.Count)
                throw new InvalidOperationException(_loc.GetString("Err_ProductNotInPersonalCatalog"));
        }

        var ownerUserId = currentUser.Role == UserRole.Admin ? (Guid?)null : currentUser.Id;
        var dish = new Dish(name, categoryId, description, ownerUserId);
        foreach (var ingredient in input)
            dish.Ingredients.Add(new DishIngredient(ingredient.ProductId, ingredient.Amount, ingredient.Unit));

        db.Dishes.Add(dish);
        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            currentUser.Id,
            ActionType.Create,
            currentUser.Role == UserRole.Admin ? "Created global dish" : "Created personal dish",
            "Dish",
            dish.Id,
            cancellationToken: cancellationToken);

        return dish;
    }

    public async Task UpdateAsync(
        Guid id,
        string name,
        Guid categoryId,
        string? description,
        IEnumerable<DishIngredientInput> ingredients,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var currentUser = _session.CurrentUser!;
        var input = ValidateIngredients(ingredients).ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var dish = await db.Dishes
                .Include(x => x.Ingredients)
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_DishNotFound"));

        if (currentUser.Role == UserRole.Admin)
        {
            if (!dish.IsGlobal)
                throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }
        else if (dish.OwnerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }

        var category = await db.Categories
            .SingleOrDefaultAsync(x => x.Id == categoryId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));

        if (currentUser.Role == UserRole.Admin)
        {
            if (!category.IsGlobal)
                throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }
        else if (!category.IsGlobal && category.OwnerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }

        var productIds = input.Select(x => x.ProductId).Distinct().ToList();
        var count = await db.Products.CountAsync(x => productIds.Contains(x.Id) && x.IsActive && x.IsGlobal, cancellationToken);
        if (count != productIds.Count)
            throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));

        if (currentUser.Role == UserRole.User)
        {
            var personalProductCount = await db.UserCatalogProducts
                .CountAsync(link => link.UserId == currentUser.Id && productIds.Contains(link.ProductId), cancellationToken);
            if (personalProductCount != productIds.Count)
                throw new InvalidOperationException(_loc.GetString("Err_ProductNotInPersonalCatalog"));
        }

        dish.SetName(name);
        dish.SetCategory(categoryId);
        dish.SetDescription(description);
        db.DishIngredients.RemoveRange(dish.Ingredients);
        dish.Ingredients.Clear();
        foreach (var ingredient in input)
            dish.Ingredients.Add(new DishIngredient(ingredient.ProductId, ingredient.Amount, ingredient.Unit));

        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(currentUser.Id, ActionType.Update, "Updated dish", "Dish", id, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var currentUser = _session.CurrentUser!;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var dish = await db.Dishes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_DishNotFound"));

        if (currentUser.Role == UserRole.Admin)
        {
            if (!dish.IsGlobal)
                throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }
        else if (dish.OwnerUserId != currentUser.Id)
        {
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
        }

        dish.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(currentUser.Id, ActionType.Delete, "Deactivated dish", "Dish", id, cancellationToken: cancellationToken);
    }

    private IEnumerable<DishIngredientInput> ValidateIngredients(IEnumerable<DishIngredientInput> ingredients)
    {
        var materialized = ingredients.ToList();
        if (materialized.Count == 0)
            throw new ArgumentException(_loc.GetString("Err_ProductRequiredInDish"));

        foreach (var item in materialized)
        {
            if (item.ProductId == Guid.Empty)
                throw new ArgumentException(_loc.GetString("Err_ProductNotSelected"));
            if (item.Amount <= 0m)
                throw new ArgumentOutOfRangeException(nameof(item.Amount), _loc.GetString("Err_PositiveIngredientAmount"));
            if (string.IsNullOrWhiteSpace(item.Unit))
                throw new ArgumentException(_loc.GetString("Err_EmptyUnit"));
        }

        return materialized;
    }

    private void EnsureAuthenticated()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }
}
