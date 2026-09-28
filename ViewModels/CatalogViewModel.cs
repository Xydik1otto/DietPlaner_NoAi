using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public partial class DishIngredientViewModel : ObservableObject
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Unit { get; set; } = "г";
}

public partial class CatalogViewModel : ViewModelBase
{
    private readonly IProductService _productService;
    private readonly IDishService _dishService;
    private readonly ICategoryService _categoryService;
    private readonly ISortService _sortService;
    private readonly IUndoService _undoService;
    private readonly IAuthenticationService _authService;
    private readonly ILoggingService _loggingService;
    private readonly IOpenFoodFactsService _openFoodFactsService;
    private readonly ILocalizationService _loc;

    private Category _allCategoriesOption;

    [ObservableProperty] private ObservableCollection<Category> _filterCategories = new();
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private string _selectedSortOption = string.Empty;
    [ObservableProperty] private bool _includeDishes = true;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<Category> _categories = new();
    [ObservableProperty] private ObservableCollection<FoodItemDisplayDto> _filteredItems = new();
    [ObservableProperty] private FoodItemDisplayDto? _selectedItem;

    // API Search Dialog
    [ObservableProperty] private bool _isApiSearchDialogOpen;
    [ObservableProperty] private string _apiSearchQuery = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotApiLoading))]
    private bool _isApiLoading;

    public bool IsNotApiLoading => !IsApiLoading;
    [ObservableProperty] private ObservableCollection<ExternalProductDto> _apiSearchResults = new();
    [ObservableProperty] private ExternalProductDto? _selectedApiProduct;
    [ObservableProperty] private string _apiStatusMessage = string.Empty;

    // Category Dialog
    [ObservableProperty] private bool _isCategoryDialogOpen;
    [ObservableProperty] private string _newCategoryName = string.Empty;
    [ObservableProperty] private string _newCategoryDescription = string.Empty;

    // Product Dialog
    [ObservableProperty] private bool _isProductDialogOpen;
    [ObservableProperty] private bool _isEditingProduct;
    [ObservableProperty] private Guid? _editingProductId;
    [ObservableProperty] private string _productName = string.Empty;
    [ObservableProperty] private Category? _productSelectedCategory;
    [ObservableProperty] private string _productDescription = string.Empty;
    [ObservableProperty] private NutritionBasis _productBasis = NutritionBasis.Per100Grams;
    [ObservableProperty] private string _productReferenceAmount = "100";
    [ObservableProperty] private string _productCalories = "100";
    [ObservableProperty] private string _productProteins = "10";
    [ObservableProperty] private string _productFats = "5";
    [ObservableProperty] private string _productCarbs = "10";
    [ObservableProperty] private string _productAllergens = string.Empty;
    [ObservableProperty] private string _productDietaryTags = string.Empty;

    // Dish Dialog
    [ObservableProperty] private bool _isDishDialogOpen;
    [ObservableProperty] private bool _isEditingDish;
    [ObservableProperty] private Guid? _editingDishId;
    [ObservableProperty] private string _dishName = string.Empty;
    [ObservableProperty] private Category? _dishSelectedCategory;
    [ObservableProperty] private string _dishDescription = string.Empty;
    [ObservableProperty] private ObservableCollection<DishIngredientViewModel> _dishIngredients = new();
    [ObservableProperty] private ObservableCollection<Product> _availableProducts = new();
    [ObservableProperty] private Product? _selectedIngredientProduct;
    [ObservableProperty] private string _ingredientAmount = "100";

    public NutritionBasis[] NutritionBases => Enum.GetValues<NutritionBasis>();
    public ObservableCollection<string> SortOptions { get; private set; } = new();

    public CatalogViewModel(
        IProductService productService,
        IDishService dishService,
        ICategoryService categoryService,
        ISortService sortService,
        IUndoService undoService,
        IAuthenticationService authService,
        ILoggingService loggingService,
        IOpenFoodFactsService openFoodFactsService,
        ILocalizationService loc)
    {
        _productService = productService;
        _dishService = dishService;
        _categoryService = categoryService;
        _sortService = sortService;
        _undoService = undoService;
        _authService = authService;
        _loggingService = loggingService;
        _openFoodFactsService = openFoodFactsService;
        _loc = loc;

        _allCategoriesOption = new Category(_loc.GetString("Cat_AllCategories"), string.Empty);
        InitializeSortOptions();

        _loc.CultureChanged += (_, _) =>
        {
            _allCategoriesOption = new Category(_loc.GetString("Cat_AllCategories"), string.Empty);
            InitializeSortOptions();
            _ = LoadDataAsync();
        };
    }

    private void InitializeSortOptions()
    {
        SortOptions = new ObservableCollection<string>
        {
            _loc.GetString("Cat_Sort_NameAsc"),
            _loc.GetString("Cat_Sort_NameDesc"),
            _loc.GetString("Cat_Sort_CalAsc"),
            _loc.GetString("Cat_Sort_CalDesc"),
            _loc.GetString("Cat_Sort_ProtDesc")
        };
        SelectedSortOption = SortOptions.FirstOrDefault() ?? string.Empty;
    }

    [RelayCommand]
    private void OpenApiSearchDialog()
    {
        ApiSearchQuery = SearchQuery;
        ApiSearchResults.Clear();
        SelectedApiProduct = null;

        var window = new Views.ApiSearchWindow
        {
            DataContext = this,
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private void CloseApiSearchDialog() => IsApiSearchDialogOpen = false;

    [RelayCommand]
    private async Task SearchApiAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ApiSearchQuery)) return;
        if (IsApiLoading) return;

        IsApiLoading = true;
        ApiSearchResults.Clear();
        SelectedApiProduct = null;

        try
        {
            var results = await _openFoodFactsService.SearchAsync(ApiSearchQuery, cancellationToken);
            if (results.Count > 0)
            {
                ApiSearchResults = new ObservableCollection<ExternalProductDto>(results);
                SelectedApiProduct = ApiSearchResults.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            ApiStatusMessage = ex.Message;
        }
        finally
        {
            IsApiLoading = false;
        }
    }

    [RelayCommand]
    private async Task ImportApiProductAsync(System.Windows.Window? dialogWindow, CancellationToken cancellationToken = default)
    {
        if (SelectedApiProduct == null) return;

        var defaultCategory = Categories.FirstOrDefault(c => c.Name != _allCategoriesOption.Name) ?? Categories.FirstOrDefault();
        if (defaultCategory == null) return;

        try
        {
            await _productService.CreateAsync(
                SelectedApiProduct.Name,
                defaultCategory.Id,
                $"OpenFoodFacts Code: {SelectedApiProduct.Code}",
                NutritionBasis.Per100Grams,
                100m,
                SelectedApiProduct.Calories,
                SelectedApiProduct.Proteins,
                SelectedApiProduct.Fats,
                SelectedApiProduct.Carbs,
                Array.Empty<string>(),
                Array.Empty<string>(),
                cancellationToken);

            dialogWindow?.Close();
            await ApplyFiltersAndSearchAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    partial void OnSearchQueryChanged(string value) => _ = ApplyFiltersAndSearchAsync();
    partial void OnSelectedCategoryChanged(Category? value) => _ = ApplyFiltersAndSearchAsync();
    partial void OnSelectedSortOptionChanged(string value) => _ = ApplyFiltersAndSearchAsync();
    partial void OnIncludeDishesChanged(bool value) => _ = ApplyFiltersAndSearchAsync();

    [RelayCommand]
    public async Task LoadDataAsync(CancellationToken cancellationToken = default)
    {
        var cats = await _categoryService.GetAllAsync(cancellationToken: cancellationToken);
        Categories = new ObservableCollection<Category>(cats);

        var filterList = new List<Category> { _allCategoriesOption };
        filterList.AddRange(cats);
        FilterCategories = new ObservableCollection<Category>(filterList);

        SelectedCategory = _allCategoriesOption;
        await ApplyFiltersAndSearchAsync(cancellationToken);
    }

    [RelayCommand]
    public async Task ApplyFiltersAndSearchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allProducts = await _productService.GetAllAsync(cancellationToken: cancellationToken);
            var productsQuery = allProducts.AsEnumerable();

            if (SelectedCategory != null && SelectedCategory != _allCategoriesOption && SelectedCategory.Id != Guid.Empty)
            {
                productsQuery = productsQuery.Where(p => p.CategoryId == SelectedCategory.Id);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var query = SearchQuery.Trim();
                productsQuery = productsQuery.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var dtos = productsQuery.Select(p => new FoodItemDisplayDto
            {
                Id = p.Id,
                Name = p.Name,
                CategoryName = p.Category?.Name ?? _loc.GetString("Cat_Uncategorized"),
                Calories = (double)p.Calories,
                Proteins = (double)p.ProteinG,
                Fats = (double)p.FatG,
                Carbs = (double)p.CarbsG,
                IsDish = false
            }).ToList();

            if (IncludeDishes)
            {
                var allDishes = await _dishService.GetAllAsync(cancellationToken: cancellationToken);
                var dishesQuery = allDishes.AsEnumerable();

                if (SelectedCategory != null && SelectedCategory != _allCategoriesOption && SelectedCategory.Id != Guid.Empty)
                {
                    dishesQuery = dishesQuery.Where(d => d.CategoryId == SelectedCategory.Id);
                }

                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    var query = SearchQuery.Trim();
                    dishesQuery = dishesQuery.Where(d => 
                        d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        d.Ingredients.Any(i => i.Product != null && i.Product.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
                }

                foreach (var d in dishesQuery)
                {
                    var nutrition = d.CalculateNutrition();
                    dtos.Add(new FoodItemDisplayDto
                    {
                        Id = d.Id,
                        Name = d.Name,
                        CategoryName = d.Category?.Name ?? _loc.GetString("Cat_Uncategorized"),
                        Calories = (double)nutrition.Calories,
                        Proteins = (double)nutrition.ProteinG,
                        Fats = (double)nutrition.FatG,
                        Carbs = (double)nutrition.CarbsG,
                        IsDish = true
                    });
                }
            }

            if (SelectedSortOption == _loc.GetString("Cat_Sort_NameDesc"))
                dtos = dtos.OrderByDescending(x => x.Name).ToList();
            else if (SelectedSortOption == _loc.GetString("Cat_Sort_CalAsc"))
                dtos = dtos.OrderBy(x => x.Calories).ThenBy(x => x.Name).ToList();
            else if (SelectedSortOption == _loc.GetString("Cat_Sort_CalDesc"))
                dtos = dtos.OrderByDescending(x => x.Calories).ThenBy(x => x.Name).ToList();
            else if (SelectedSortOption == _loc.GetString("Cat_Sort_ProtDesc"))
                dtos = dtos.OrderByDescending(x => x.Proteins).ThenBy(x => x.Name).ToList();
            else
                dtos = dtos.OrderBy(x => x.Name).ToList();

            FilteredItems = new ObservableCollection<FoodItemDisplayDto>(dtos);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenAddCategoryDialog()
    {
        NewCategoryName = string.Empty;
        NewCategoryDescription = string.Empty;
        IsCategoryDialogOpen = true;
    }

    [RelayCommand]
    private void CloseCategoryDialog() => IsCategoryDialogOpen = false;

    [RelayCommand]
    private async Task SaveCategoryAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;

        try
        {
            await _categoryService.CreateAsync(NewCategoryName, NewCategoryDescription, cancellationToken);
            IsCategoryDialogOpen = false;
            await LoadDataAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenAddProductDialog()
    {
        IsEditingProduct = false;
        EditingProductId = null;
        ProductName = string.Empty;
        ProductSelectedCategory = Categories.FirstOrDefault();
        ProductDescription = string.Empty;
        ProductBasis = NutritionBasis.Per100Grams;
        ProductReferenceAmount = "100";
        ProductCalories = "100";
        ProductProteins = "10";
        ProductFats = "5";
        ProductCarbs = "10";
        ProductAllergens = string.Empty;
        ProductDietaryTags = string.Empty;

        IsProductDialogOpen = true;
    }

    [RelayCommand]
    private async Task OpenEditProductDialogAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedItem == null || SelectedItem.IsDish) return;

        var products = await _productService.GetAllAsync(cancellationToken: cancellationToken);
        var product = products.FirstOrDefault(p => p.Id == SelectedItem.Id);
        if (product == null) return;

        IsEditingProduct = true;
        EditingProductId = product.Id;
        ProductName = product.Name;
        ProductSelectedCategory = Categories.FirstOrDefault(c => c.Id == product.CategoryId);
        ProductDescription = product.Description ?? string.Empty;
        ProductBasis = product.Basis;
        ProductReferenceAmount = product.ReferenceAmount.ToString("0.##");
        ProductCalories = product.Calories.ToString("0.##");
        ProductProteins = product.ProteinG.ToString("0.##");
        ProductFats = product.FatG.ToString("0.##");
        ProductCarbs = product.CarbsG.ToString("0.##");
        ProductAllergens = string.Join(", ", product.Allergens);
        ProductDietaryTags = string.Join(", ", product.DietaryTags);

        IsProductDialogOpen = true;
    }

    [RelayCommand]
    private void CloseProductDialog() => IsProductDialogOpen = false;

    [RelayCommand]
    private async Task SaveProductAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProductName) || ProductSelectedCategory == null) return;

        if (!decimal.TryParse(ProductReferenceAmount, out var refAmt) ||
            !decimal.TryParse(ProductCalories, out var cal) ||
            !decimal.TryParse(ProductProteins, out var prot) ||
            !decimal.TryParse(ProductFats, out var fat) ||
            !decimal.TryParse(ProductCarbs, out var carbs))
        {
            return;
        }

        var allergens = ProductAllergens.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = ProductDietaryTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            if (IsEditingProduct && EditingProductId.HasValue)
            {
                await _productService.UpdateAsync(
                    EditingProductId.Value,
                    ProductName,
                    ProductSelectedCategory.Id,
                    ProductDescription,
                    ProductBasis,
                    refAmt,
                    cal,
                    prot,
                    fat,
                    carbs,
                    allergens,
                    tags,
                    cancellationToken);
            }
            else
            {
                await _productService.CreateAsync(
                    ProductName,
                    ProductSelectedCategory.Id,
                    ProductDescription,
                    ProductBasis,
                    refAmt,
                    cal,
                    prot,
                    fat,
                    carbs,
                    allergens,
                    tags,
                    cancellationToken);
            }

            IsProductDialogOpen = false;
            await ApplyFiltersAndSearchAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenAddDishDialogAsync(CancellationToken cancellationToken = default)
    {
        IsEditingDish = false;
        EditingDishId = null;
        DishName = string.Empty;
        DishSelectedCategory = Categories.FirstOrDefault();
        DishDescription = string.Empty;
        IngredientAmount = "100";
        DishIngredients.Clear();

        var prods = await _productService.GetAllAsync(cancellationToken: cancellationToken);
        AvailableProducts = new ObservableCollection<Product>(prods);
        SelectedIngredientProduct = AvailableProducts.FirstOrDefault();

        IsDishDialogOpen = true;
    }

    [RelayCommand]
    private async Task OpenEditDishDialogAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedItem == null || !SelectedItem.IsDish) return;

        var dishes = await _dishService.GetAllAsync(cancellationToken: cancellationToken);
        var dish = dishes.FirstOrDefault(d => d.Id == SelectedItem.Id);
        if (dish == null) return;

        IsEditingDish = true;
        EditingDishId = dish.Id;
        DishName = dish.Name;
        DishSelectedCategory = Categories.FirstOrDefault(c => c.Id == dish.CategoryId);
        DishDescription = dish.Description ?? string.Empty;

        var prods = await _productService.GetAllAsync(cancellationToken: cancellationToken);
        AvailableProducts = new ObservableCollection<Product>(prods);
        SelectedIngredientProduct = AvailableProducts.FirstOrDefault();

        DishIngredients.Clear();
        foreach (var ing in dish.Ingredients)
        {
            DishIngredients.Add(new DishIngredientViewModel
            {
                ProductId = ing.ProductId,
                ProductName = ing.Product?.Name ?? string.Empty,
                Amount = ing.Amount,
                Unit = ing.Unit
            });
        }

        IsDishDialogOpen = true;
    }

    [RelayCommand]
    private void AddDishIngredient()
    {
        if (SelectedIngredientProduct == null) return;
        if (!decimal.TryParse(IngredientAmount, out var amt) || amt <= 0) return;

        DishIngredients.Add(new DishIngredientViewModel
        {
            ProductId = SelectedIngredientProduct.Id,
            ProductName = SelectedIngredientProduct.Name,
            Amount = amt,
            Unit = "г"
        });
    }

    [RelayCommand]
    private void RemoveDishIngredient(DishIngredientViewModel? ing)
    {
        if (ing != null) DishIngredients.Remove(ing);
    }

    [RelayCommand]
    private void CloseDishDialog() => IsDishDialogOpen = false;

    [RelayCommand]
    private async Task SaveDishAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(DishName) || DishSelectedCategory == null) return;
        if (DishIngredients.Count == 0) return;

        var inputs = DishIngredients.Select(i => new DishIngredientInput(i.ProductId, i.Amount, i.Unit));

        try
        {
            if (IsEditingDish && EditingDishId.HasValue)
            {
                await _dishService.UpdateAsync(
                    EditingDishId.Value,
                    DishName,
                    DishSelectedCategory.Id,
                    DishDescription,
                    inputs,
                    cancellationToken);
            }
            else
            {
                await _dishService.CreateAsync(
                    DishName,
                    DishSelectedCategory.Id,
                    DishDescription,
                    inputs,
                    cancellationToken);
            }

            IsDishDialogOpen = false;
            await ApplyFiltersAndSearchAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedItemAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedItem == null) return;

        if (SelectedItem.IsDish)
        {
            await _dishService.DeleteAsync(SelectedItem.Id, cancellationToken);
        }
        else
        {
            await _productService.DeleteAsync(SelectedItem.Id, cancellationToken);
        }

        await ApplyFiltersAndSearchAsync(cancellationToken);
    }
}