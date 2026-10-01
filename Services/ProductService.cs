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

    public ProductService(
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

    public async Task<IReadOnlyList<Product>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        if (_session.CurrentUser?.Role == UserRole.Admin)
            return await GetGlobalAsync(includeInactive, cancellationToken);

        var userId = _session.CurrentUser!.Id;
        return await GetForUserAsync(userId, includeInactive, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetGlobalAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Where(x => x.IsGlobal)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        var products = await query
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        foreach (var product in products)
            product.RestoreCollectionsFromStorage();

        return products;
    }

    public async Task<IReadOnlyList<Product>> GetForUserAsync(
        Guid userId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        EnsureSameUserOrAdmin(userId);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Where(x => x.IsGlobal && x.UserCatalogItems.Any(link => link.UserId == userId))
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        var products = await query
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        foreach (var product in products)
            product.RestoreCollectionsFromStorage();

        return products;
    }

    public async Task AddToPersonalCatalogAsync(
        Guid productId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var targetUserId = userId ?? _session.CurrentUser!.Id;
        EnsureSameUserOrAdmin(targetUserId);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products
            .SingleOrDefaultAsync(x => x.Id == productId && x.IsActive && x.IsGlobal, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));

        var exists = await db.UserCatalogProducts
            .AnyAsync(x => x.UserId == targetUserId && x.ProductId == productId, cancellationToken);
        if (exists)
            return;

        db.UserCatalogProducts.Add(new UserCatalogProduct(targetUserId, product.Id));
        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            _session.CurrentUser?.Id,
            ActionType.Create,
            $"Added product '{product.Name}' to personal catalog.",
            "UserCatalogProduct",
            product.Id,
            cancellationToken: cancellationToken);
    }

    public async Task RemoveFromPersonalCatalogAsync(
        Guid productId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var targetUserId = userId ?? _session.CurrentUser!.Id;
        EnsureSameUserOrAdmin(targetUserId);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var link = await db.UserCatalogProducts
            .Include(x => x.Product)
            .SingleOrDefaultAsync(x => x.UserId == targetUserId && x.ProductId == productId, cancellationToken);

        if (link == null)
            throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));

        db.UserCatalogProducts.Remove(link);
        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            _session.CurrentUser?.Id,
            ActionType.Delete,
            $"Removed product '{link.Product?.Name ?? productId.ToString()}' from personal catalog.",
            "UserCatalogProduct",
            productId,
            cancellationToken: cancellationToken);
    }

    public async Task<Product> CreateAsync(
        string name,
        Guid categoryId,
        string? description,
        NutritionBasis basis,
        decimal referenceAmount,
        decimal calories,
        decimal proteinG,
        decimal fatG,
        decimal carbsG,
        IEnumerable<string> allergens,
        IEnumerable<string> dietaryTags,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        ValidateNutrition(referenceAmount, calories, proteinG, fatG, carbsG);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var category = await db.Categories
            .SingleOrDefaultAsync(x => x.Id == categoryId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));

        // Product is a globally shared entity (personal ownership is represented by
        // UserCatalogProduct), so its canonical category must also be global.
        // Personal categories are intentionally reserved for personal dishes.
        if (!category.IsGlobal)
            throw new InvalidOperationException(_loc.GetString("Err_GlobalCategoryRequired"));

        var product = new Product(name, categoryId, basis, calories, proteinG, fatG, carbsG);
        product.SetDescription(description);
        product.SetReferenceAmount(referenceAmount);
        product.SetAllergens(allergens);
        product.SetDietaryTags(dietaryTags);

        db.Products.Add(product);

        var currentUser = _session.CurrentUser!;
        if (currentUser.Role == UserRole.User)
            db.UserCatalogProducts.Add(new UserCatalogProduct(currentUser.Id, product.Id));

        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            currentUser.Id,
            ActionType.Create,
            currentUser.Role == UserRole.Admin
                ? "Created global product."
                : "Created global product and synchronized it to the personal catalog.",
            "Product",
            product.Id,
            cancellationToken: cancellationToken);

        return product;
    }

    public async Task UpdateAsync(
        Guid id,
        string name,
        Guid categoryId,
        string? description,
        NutritionBasis basis,
        decimal referenceAmount,
        decimal calories,
        decimal proteinG,
        decimal fatG,
        decimal carbsG,
        IEnumerable<string> allergens,
        IEnumerable<string> dietaryTags,
        CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        ValidateNutrition(referenceAmount, calories, proteinG, fatG, carbsG);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == id && x.IsGlobal, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));

        var category = await db.Categories
            .SingleOrDefaultAsync(x => x.Id == categoryId && x.IsActive && x.OwnerUserId == null, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_CategoryNotFound"));

        product.SetName(name);
        product.SetCategory(category.Id);
        product.SetDescription(description);
        product.SetBasis(basis);
        product.SetReferenceAmount(referenceAmount);
        product.SetNutrition(calories, proteinG, fatG, carbsG);
        product.SetAllergens(allergens);
        product.SetDietaryTags(dietaryTags);

        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Update, "Updated global product", "Product", id, cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        if (_session.CurrentUser?.Role == UserRole.User)
        {
            await RemoveFromPersonalCatalogAsync(id, _session.CurrentUser.Id, cancellationToken);
            return;
        }

        EnsureAdmin();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == id && x.IsGlobal, cancellationToken)
            ?? throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));

        product.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        await _logging.LogActionAsync(_session.CurrentUser?.Id, ActionType.Delete, "Deactivated global product", "Product", id, cancellationToken: cancellationToken);
    }

    private void EnsureAuthenticated()
    {
        if (!_session.IsAuthenticated)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }

    private void EnsureAdmin()
    {
        EnsureAuthenticated();
        if (_session.CurrentUser?.Role != UserRole.Admin)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }

    private void EnsureSameUserOrAdmin(Guid userId)
    {
        EnsureAuthenticated();
        if (_session.CurrentUser?.Role == UserRole.Admin)
            return;
        if (_session.CurrentUser?.Id != userId)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
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
}
