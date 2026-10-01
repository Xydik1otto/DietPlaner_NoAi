using System.Globalization;
using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace DietPlanner.Tests.Infrastructure;

internal sealed class StubLocalizationService : ILocalizationService
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal)
    {
        ["Err_Unauthorized"] = "Unauthorized",
        ["Err_ProductNotFound"] = "Product not found",
        ["Err_ProductNotInPersonalCatalog"] = "Product is not in personal catalog",
        ["Err_CategoryNotFound"] = "Category not found",
        ["Err_DishNotFound"] = "Dish not found",
        ["Err_CategoryExists"] = "Category exists",
        ["Err_ProductRequiredInDish"] = "Product required",
        ["Err_ProductNotSelected"] = "Product not selected",
        ["Err_PositiveIngredientAmount"] = "Positive amount required",
        ["Err_EmptyUnit"] = "Unit required",
        ["Err_GlobalCategoryRequired"] = "Global category required",
        ["Err_NegativeNutrition"] = "Nutrition cannot be negative",
        ["Err_PositiveReferenceAmount"] = "Reference amount must be positive",
        ["Err_PositivePortion"] = "Portion must be positive",
        ["Err_FillProfileCalc"] = "Fill profile",
        ["Prof_VerificationSent"] = "Verification code sent",
        ["Err_AgeRestriction"] = "Invalid age",
        ["Err_PositiveHeightWeight"] = "Height and weight must be positive",
        ["Err_UserNotFound"] = "User not found",
        ["Plan_NoItemsError"] = "No items",
        ["Plan_AddDishesHint"] = "Add dishes",
        ["Plan_LogCreated"] = "Created plan {0}, {1}, {2}",
        ["Plan_GenSuccess"] = "Generated plan {0}, {1}",
        ["Unit_Grams"] = "g",
        ["Meal_Single"] = "Main meal",
        ["Meal_Breakfast"] = "Breakfast",
        ["Meal_Lunch"] = "Lunch",
        ["Meal_Dinner"] = "Dinner",
        ["Meal_Snack"] = "Snack",
        ["Meal_SecondBreakfast"] = "Second breakfast",
        ["Meal_LateDinner"] = "Late dinner",
        ["Meal_Snack2"] = "Second snack",
        ["Meal_Snack3"] = "Third snack",
        ["Val_RequiredField"] = "{0} is required",
        ["Val_MaxLengthExceeded"] = "{0} must not exceed {1} characters",
        ["Val_InvalidNumber"] = "{0} is invalid",
        ["Val_EnterValidNumber"] = "Enter a valid {0}",
        ["Val_RangeExceeded"] = "{0} must be between {1} and {2}",
        ["Val_InvalidEmailFormat"] = "Invalid {0} format",
        ["Val_MinPasswordLength"] = "{0} must be at least {1} characters",
        ["Auth_EmptyEmailPassword"] = "Email and password are required",
        ["Auth_UserNotRegistered"] = "User not registered",
        ["Auth_IncorrectPassword"] = "Incorrect password",
        ["Auth_FillRequiredFields"] = "Fill required fields",
        ["Auth_PasswordMismatchErr"] = "Passwords do not match",
        ["Auth_EmailAlreadyRegistered"] = "Email is already registered"
    };

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.InvariantCulture;

    public IReadOnlyList<CultureInfo> SupportedCultures { get; } =
        [CultureInfo.InvariantCulture, new CultureInfo("uk-UA"), new CultureInfo("en-US")];

    public event EventHandler<CultureInfo>? CultureChanged;

    public void SetCulture(CultureInfo culture)
    {
        CurrentCulture = culture;
        CultureChanged?.Invoke(this, culture);
    }

    public string GetString(string key) => _values.TryGetValue(key, out var value) ? value : key;
}

internal sealed class NoOpLoggingService : ILoggingService
{
    public readonly List<(ActionType ActionType, string Description, string? EntityName, Guid? EntityId)> Actions = [];

    public Task LogActionAsync(
        Guid? userId,
        ActionType actionType,
        string description,
        string? entityName = null,
        Guid? entityId = null,
        string? beforeJson = null,
        string? afterJson = null,
        CancellationToken cancellationToken = default)
    {
        Actions.Add((actionType, description, entityName, entityId));
        return Task.CompletedTask;
    }

    public Task LogErrorAsync(Guid? userId, Exception exception, string context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class AllowAllRestrictionService : IRestrictionService
{
    public Task<IReadOnlyList<DietaryRestriction>> GetAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DietaryRestriction>>([]);

    public Task<IReadOnlyList<DietaryRestriction>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DietaryRestriction>>([]);

    public Task SetUserRestrictionsAsync(Guid userId, IEnumerable<Guid> restrictionIds, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AddForbiddenProductAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveForbiddenProductAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<RestrictionCheckResult> CheckProductAsync(Guid userId, Product product, CancellationToken cancellationToken = default)
        => Task.FromResult(new RestrictionCheckResult(true, string.Empty));

    public Task<RestrictionCheckResult> CheckDishAsync(Guid userId, Dish dish, CancellationToken cancellationToken = default)
        => Task.FromResult(new RestrictionCheckResult(true, string.Empty));
}

internal sealed class FixedNutritionCalculator : INutritionCalculator
{
    private readonly decimal _targetCalories;

    public FixedNutritionCalculator(decimal targetCalories = 2000m)
    {
        _targetCalories = targetCalories;
    }

    public NutritionCalculation Calculate(User user) =>
        new(
            25,
            1500m,
            2000m,
            _targetCalories,
            new NutritionTargets(_targetCalories, _targetCalories * 0.075m, _targetCalories * 0.0277777778m, _targetCalories * 0.1125m),
            22m);

    public NutritionSnapshot CalculateProductPortion(Product product, decimal amount) => product.CalculateNutritionSnapshot(amount);

    public NutritionSnapshot CalculateDishPortion(Dish dish, decimal portionMultiplier)
    {
        if (portionMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(portionMultiplier));

        var nutrition = dish.CalculateNutrition();
        return new NutritionSnapshot(
            nutrition.Calories * portionMultiplier,
            nutrition.ProteinG * portionMultiplier,
            nutrition.FatG * portionMultiplier,
            nutrition.CarbsG * portionMultiplier);
    }
}

internal sealed class InMemoryDbContextFactory : IDbContextFactory<AppDbContext>, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    private InMemoryDbContextFactory(SqliteConnection connection, DbContextOptions<AppDbContext> options)
    {
        _connection = connection;
        _options = options;
    }

    public static async Task<InMemoryDbContextFactory> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        return new InMemoryDbContextFactory(connection, options);
    }

    public AppDbContext CreateDbContext() => new(_options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AppDbContext>(new AppDbContext(_options));

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}

internal static class TestDataFactory
{
    public static User CreateUser(UserRole role = UserRole.User)
    {
        var user = new User("test@example.com", "Test User", "hash", role);
        user.UpdateNutritionProfile(
            DateTime.UtcNow.Date.AddYears(-25),
            Sex.Male,
            180m,
            75m,
            ActivityLevel.Moderate,
            NutritionGoal.MaintainWeight,
            null,
            null);
        return user;
    }

    public static Category CreateGlobalCategory(string name = "Food") => new(name);

    public static Product CreateProduct(
        Guid categoryId,
        string name = "Chicken",
        decimal calories = 165m,
        decimal protein = 31m,
        decimal fat = 3.6m,
        decimal carbs = 0m)
        => new(name, categoryId, NutritionBasis.Per100Grams, calories, protein, fat, carbs);
}
