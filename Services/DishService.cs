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

    public DishService(IDbContextFactory<AppDbContext> dbFactory, SessionService session, ILoggingService logging)
    {
        _dbFactory = dbFactory;
        _session = session;
        _logging = logging;
    }

    public async Task<IReadOnlyList<Dish>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Dishes.AsNoTracking().Include(x => x.Category).Include(x => x.Ingredients).ThenInclude(x => x.Product).AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<Dish> CreateAsync(string name, Guid categoryId, string? description, IEnumerable<DishIngredientInput> ingredients, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var input = ValidateIngredients(ingredients).ToList();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Categories.AnyAsync(x => x.Id == categoryId && x.IsActive, cancellationToken))
            throw new KeyNotFoundException("Категорію не знайдено або вона неактивна.");

        var productIds = input.Select(x => x.ProductId).Distinct().ToArray();
        var count = await db.Products.CountAsync(x => productIds.Contains(x.Id) && x.IsActive, cancellationToken);
        if (count != productIds.Length) throw new KeyNotFoundException("Один або декілька продуктів не знайдені.");

        var dish = new Dish(name, categoryId, description);
        foreach (var ingredient in input) dish.Ingredients.Add(new DishIngredient(ingredient.ProductId, ingredient.Amount, ingredient.Unit));
        db.Dishes.Add(dish);
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Create, "Created dish", "Dish", dish.Id, cancellationToken: cancellationToken);
        return dish;
    }

    public async Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, IEnumerable<DishIngredientInput> ingredients, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var input = ValidateIngredients(ingredients).ToList();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var dish = await db.Dishes.Include(x => x.Ingredients).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Страву не знайдено.");
        if (!await db.Categories.AnyAsync(x => x.Id == categoryId && x.IsActive, cancellationToken))
            throw new KeyNotFoundException("Категорію не знайдено або вона неактивна.");

        var productIds = input.Select(x => x.ProductId).Distinct().ToArray();
        var count = await db.Products.CountAsync(x => productIds.Contains(x.Id) && x.IsActive, cancellationToken);
        if (count != productIds.Length) throw new KeyNotFoundException("Один або декілька продуктів не знайдені.");

        dish.SetName(name);
        dish.SetCategory(categoryId);
        dish.SetDescription(description);
        db.DishIngredients.RemoveRange(dish.Ingredients);
        dish.Ingredients.Clear();
        foreach (var ingredient in input) dish.Ingredients.Add(new DishIngredient(ingredient.ProductId, ingredient.Amount, ingredient.Unit));
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Update, "Updated dish", "Dish", id, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var dish = await db.Dishes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Страву не знайдено.");
        dish.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Delete, "Deactivated dish", "Dish", id, cancellationToken: cancellationToken);
    }

    private static IEnumerable<DishIngredientInput> ValidateIngredients(IEnumerable<DishIngredientInput> ingredients)
    {
        var materialized = ingredients.ToList();
        if (materialized.Count == 0) throw new ArgumentException("Страва повинна містити хоча б один продукт.");
        foreach (var item in materialized)
        {
            if (item.ProductId == Guid.Empty) throw new ArgumentException("Не вибрано продукт.");
            if (item.Amount <= 0m) throw new ArgumentOutOfRangeException(nameof(item.Amount), "Кількість інгредієнта має бути більшою за 0.");
            if (string.IsNullOrWhiteSpace(item.Unit)) throw new ArgumentException("Одиниця вимірювання не може бути порожньою.");
        }
        return materialized;
    }

    private void EnsureAuthenticated()
    {
        if (!_session.IsAuthenticated) throw new UnauthorizedAccessException("Потрібен вхід.");
    }
}
