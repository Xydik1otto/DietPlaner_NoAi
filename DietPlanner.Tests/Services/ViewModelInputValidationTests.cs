using System.Collections.ObjectModel;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using DietPlanner.ViewModels;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class ViewModelInputValidationTests
{
    [Fact]
    public async Task Catalog_SaveCategory_WhitespaceName_DoesNotCallService()
    {
        var categoryService = new TrackingCategoryService();
        var vm = CreateCatalogViewModel(categoryService: categoryService);
        vm.NewCategoryName = "   \t  ";
        vm.IsCategoryDialogOpen = true;

        await vm.SaveCategoryCommand.ExecuteAsync(null);

        Assert.Equal(0, categoryService.CreateCalls);
        Assert.True(vm.IsCategoryDialogOpen);
    }

    [Fact]
    public async Task Catalog_SaveProduct_EmptyName_DoesNotCallService()
    {
        var productService = new TrackingProductService();
        var vm = CreateCatalogViewModel(productService: productService);
        vm.ProductName = " ";
        vm.ProductSelectedCategory = TestDataFactory.CreateGlobalCategory();

        await vm.SaveProductCommand.ExecuteAsync(null);

        Assert.Equal(0, productService.CreateCalls);
    }

    [Fact]
    public async Task Catalog_SaveProduct_MalformedNumericInput_DoesNotCallService()
    {
        var productService = new TrackingProductService();
        var vm = CreateCatalogViewModel(productService: productService);
        vm.ProductName = "Apple";
        vm.ProductSelectedCategory = TestDataFactory.CreateGlobalCategory();
        vm.ProductReferenceAmount = "100x";

        await vm.SaveProductCommand.ExecuteAsync(null);

        Assert.Equal(0, productService.CreateCalls);
    }

    [Fact]
    public async Task Catalog_SaveProduct_AllNumericFieldsMustParse()
    {
        var productService = new TrackingProductService();
        var vm = CreateCatalogViewModel(productService: productService);
        vm.ProductName = "Apple";
        vm.ProductSelectedCategory = TestDataFactory.CreateGlobalCategory();

        var cases = new Action<string>[]
        {
            value => vm.ProductReferenceAmount = value,
            value => vm.ProductCalories = value,
            value => vm.ProductProteins = value,
            value => vm.ProductFats = value,
            value => vm.ProductCarbs = value,
        };

        foreach (var setInvalid in cases)
        {
            vm.ProductReferenceAmount = "100";
            vm.ProductCalories = "100";
            vm.ProductProteins = "10";
            vm.ProductFats = "5";
            vm.ProductCarbs = "10";
            setInvalid("not-a-number");

            await vm.SaveProductCommand.ExecuteAsync(null);
        }

        Assert.Equal(0, productService.CreateCalls);
    }

    [Fact]
    public void Catalog_AddDishIngredient_RejectsMalformedAmount()
    {
        var vm = CreateCatalogViewModel();
        vm.SelectedIngredientProduct = TestDataFactory.CreateProduct(TestDataFactory.CreateGlobalCategory().Id);
        vm.IngredientAmount = "abc";

        vm.AddDishIngredientCommand.Execute(null);

        Assert.Empty(vm.DishIngredients);
    }

    [Fact]
    public void Catalog_AddDishIngredient_RejectsZeroAndNegativeAmounts()
    {
        var vm = CreateCatalogViewModel();
        vm.SelectedIngredientProduct = TestDataFactory.CreateProduct(TestDataFactory.CreateGlobalCategory().Id);

        foreach (var value in new[] { "0", "-1", "-100" })
        {
            vm.IngredientAmount = value;
            vm.AddDishIngredientCommand.Execute(null);
        }

        Assert.Empty(vm.DishIngredients);
    }

    [Fact]
    public async Task Catalog_SaveDish_WithoutIngredients_DoesNotCallService()
    {
        var dishService = new TrackingDishService();
        var vm = CreateCatalogViewModel(dishService: dishService);
        vm.DishName = "Dish";
        vm.DishSelectedCategory = TestDataFactory.CreateGlobalCategory();
        vm.DishIngredients.Clear();

        await vm.SaveDishCommand.ExecuteAsync(null);

        Assert.Equal(0, dishService.CreateCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(100)]
    public void PlanGenerator_MealCount_IsClampedToOneThroughEight(int value)
    {
        var vm = CreatePlanGeneratorViewModel();

        vm.MealCount = value;

        Assert.InRange(vm.MealCount, 1, 8);
        Assert.Equal(value < 1 ? 1 : 8, vm.MealCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void PlanGenerator_MealCount_LeavesValidValuesUnchanged(int value)
    {
        var vm = CreatePlanGeneratorViewModel();

        vm.MealCount = value;

        Assert.Equal(value, vm.MealCount);
    }

    private static CatalogViewModel CreateCatalogViewModel(
        TrackingProductService? productService = null,
        TrackingDishService? dishService = null,
        TrackingCategoryService? categoryService = null)
    {
        return new CatalogViewModel(
            productService ?? new TrackingProductService(),
            dishService ?? new TrackingDishService(),
            categoryService ?? new TrackingCategoryService(),
            new TrackingSortService(),
            new TrackingSearchService(),
            new TrackingUndoService(),
            new TrackingAuthenticationService(TestDataFactory.CreateUser()),
            new NoOpLoggingService(),
            new TrackingOpenFoodFactsService(),
            new StubLocalizationService());
    }

    private static PlanGeneratorViewModel CreatePlanGeneratorViewModel()
    {
        return new PlanGeneratorViewModel(
            new TrackingPlanService(),
            new TrackingAuthenticationService(null),
            new FixedNutritionCalculator(),
            new TrackingProductService(),
            new TrackingDishService());
    }
}

internal sealed class TrackingNavigationService : INavigationService
{
    public event EventHandler<object>? NavigationRequested;
    public void Navigate(object viewModel) => NavigationRequested?.Invoke(this, viewModel);
    public void Navigate<TViewModel>() where TViewModel : notnull => Navigate(new object());
}

internal sealed class TrackingAuthenticationService : IAuthenticationService
{
    public TrackingAuthenticationService(User? user) => CurrentUser = user;
    public User? CurrentUser { get; }
    public bool IsAuthenticated => CurrentUser != null;
    public Task<(bool Success, string Message)> LoginAsync(string email, string password, CancellationToken cancellationToken = default) => Task.FromResult((false, "unsupported"));
    public Task<bool> TryAutoLoginAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<(bool Success, string Message)> RegisterAsync(string email, string displayName, string password, string passwordConfirmation, CancellationToken cancellationToken = default) => Task.FromResult((false, "unsupported"));
    public void Logout() { }
}

internal sealed class TrackingCategoryService : ICategoryService
{
    public int CreateCalls { get; private set; }
    public Task<IReadOnlyList<Category>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Category>>([]);
    public Task<IReadOnlyList<Category>> GetGlobalAsync(bool includeInactive = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Category>>([]);
    public Task<Category> CreateAsync(string name, string? description, CancellationToken cancellationToken = default) { CreateCalls++; return Task.FromResult(new Category(name, description)); }
    public Task UpdateAsync(Guid id, string name, string? description, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class TrackingProductService : IProductService
{
    public int CreateCalls { get; private set; }
    public Task<IReadOnlyList<Product>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>([]);
    public Task<IReadOnlyList<Product>> GetGlobalAsync(bool includeInactive = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>([]);
    public Task<IReadOnlyList<Product>> GetForUserAsync(Guid userId, bool includeInactive = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Product>>([]);
    public Task AddToPersonalCatalogAsync(Guid productId, Guid? userId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveFromPersonalCatalogAsync(Guid productId, Guid? userId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<Product> CreateAsync(string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        return Task.FromResult(TestDataFactory.CreateProduct(categoryId, name, calories, proteinG, fatG, carbsG));
    }
    public Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, NutritionBasis basis, decimal referenceAmount, decimal calories, decimal proteinG, decimal fatG, decimal carbsG, IEnumerable<string> allergens, IEnumerable<string> dietaryTags, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class TrackingDishService : IDishService
{
    public int CreateCalls { get; private set; }
    public Task<IReadOnlyList<Dish>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Dish>>([]);
    public Task<Dish> CreateAsync(string name, Guid categoryId, string? description, IEnumerable<DishIngredientInput> ingredients, CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        return Task.FromResult(new Dish(name, categoryId, description));
    }
    public Task UpdateAsync(Guid id, string name, Guid categoryId, string? description, IEnumerable<DishIngredientInput> ingredients, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class TrackingSortService : ISortService
{
    public IReadOnlyList<Product> SortProducts(IEnumerable<Product> source, SortField primary, bool primaryDescending, SortField secondary, bool secondaryDescending) => source.ToList();
}

internal sealed class TrackingSearchService : ISearchService
{
    public Task<IReadOnlyList<ProductSearchResult>> SearchProductsAsync(string query, Guid? categoryId = null, decimal? minCalories = null, decimal? maxCalories = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSearchResult>>([]);
    public Task<IReadOnlyList<ProductSearchResult>> SearchGlobalProductsAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSearchResult>>([]);
    public Task<IReadOnlyList<DishSearchResult>> SearchDishesAsync(string query, Guid? categoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DishSearchResult>>([]);
}

internal sealed class TrackingUndoService : IUndoService
{
    public void RecordAction(ActionRecord record) { }
    public Task<List<ActionRecord>> GetHistoryStackAsync() => Task.FromResult(new List<ActionRecord>());
    public Task<bool> UndoLastActionAsync() => Task.FromResult(false);
}

internal sealed class TrackingOpenFoodFactsService : IOpenFoodFactsService
{
    public Task<List<ExternalProductDto>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult(new List<ExternalProductDto>());
}

internal sealed class TrackingPlanService : IPlanService
{
    public Task<PlanGenerationResult> GenerateAsync(Guid userId, int mealCount, decimal? targetCalories = null, CancellationToken cancellationToken = default) => Task.FromResult(new PlanGenerationResult(false, "unsupported", null, null, []));
    public Task<IReadOnlyList<PlanGenerationResult>> GenerateWeekAsync(Guid userId, int mealCount, decimal? targetCalories = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlanGenerationResult>>([]);
    public Task<NutritionPlan?> GetForDateAsync(Guid userId, DateTime date, CancellationToken cancellationToken = default) => Task.FromResult<NutritionPlan?>(null);
    public Task<IReadOnlyList<NutritionPlan>> GetHistoryAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<NutritionPlan>>([]);
    public Task<bool> ReplacePlanItemAsync(Guid planItemId, Guid? newProductId, Guid? newDishId, decimal portionGrams, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
