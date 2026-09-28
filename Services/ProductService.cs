using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public sealed class ProductService : IProductService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly SessionService _session;
    private readonly ILoggingService _logging;
    private readonly ILocalizationService _loc;

    public ProductService(IDbContextFactory<AppDbContext> dbFactory, SessionService session, ILoggingService logging, ILocalizationService loc)
    {
        _dbFactory = dbFactory;
        _session = session;
        _logging = logging;
        _loc = loc;
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Products.AsNoTracking().Include(x => x.Category).AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        var products = await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
        foreach (var product in products) product.RestoreCollectionsFromStorage();
        return products;
    }

    public async Task<Product> CreateAsync(string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default)
    {
        EnsureCanEditCatalog();
        ValidateNutrition(referenceAmount, calories, proteinG, fatG, carbsG);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Categories.AnyAsync(x => x.Id == categoryId && x.IsActive, cancellationToken))
            throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));

        var product = new Product(name, categoryId, basis, calories, proteinG, fatG, carbsG);
        product.SetDescription(description);
        product.SetReferenceAmount(referenceAmount);
        product.SetAllergens(allergens);
        product.SetDietaryTags(dietaryTags);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Create, "Created product", "Product", product.Id, cancellationToken: cancellationToken);
        return product;
    }

    public async Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default)
    {
        EnsureCanEditCatalog();
        ValidateNutrition(referenceAmount, calories, proteinG, fatG, carbsG);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));
        if (!await db.Categories.AnyAsync(x => x.Id == categoryId && x.IsActive, cancellationToken))
            throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));

        product.SetName(name);
        product.SetCategory(categoryId);
        product.SetDescription(description);
        product.SetBasis(basis);
        product.SetReferenceAmount(referenceAmount);
        product.SetNutrition(calories, proteinG, fatG, carbsG);
        product.SetAllergens(allergens);
        product.SetDietaryTags(dietaryTags);
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Update, "Updated product", "Product", id, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureCanEditCatalog();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));
        product.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Delete, "Deactivated product", "Product", id, cancellationToken: cancellationToken);
    }

    private void ValidateNutrition(decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG)
    {
        foreach (var value in new[] { referenceAmount, calories, proteinG, fatG, carbsG })
        {
            if (value < 0m)
                throw new ArgumentOutOfRangeException(nameof(value), _loc.GetString("Err_NegativeNutrition"));
        }
        if (referenceAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(referenceAmount), _loc.GetString("Err_PositiveReferenceAmount"));
    }

    private void EnsureCanEditCatalog()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }
}