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

    private static readonly Category AllCategoriesOption = new("Усі категорії", string.Empty);

    [ObservableProperty] private ObservableCollection<Category> _filterCategories = new();
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private string _selectedSortOption = "Назва (А-Я)";
    [ObservableProperty] private bool _includeDishes = true;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<Category> _categories = new();
    [ObservableProperty] private ObservableCollection<FoodItemDisplayDto> _filteredItems = new();
    [ObservableProperty] private FoodItemDisplayDto? _selectedItem;

    // --- Поля модального вікна API-пошуку ---
    [ObservableProperty] private bool _isApiSearchDialogOpen;
    [ObservableProperty] private string _apiSearchQuery = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotApiLoading))]
    private bool _isApiLoading;

    public bool IsNotApiLoading => !IsApiLoading;
    [ObservableProperty] private ObservableCollection<ExternalProductDto> _apiSearchResults = new();
    [ObservableProperty] private ExternalProductDto? _selectedApiProduct;
    [ObservableProperty] private string _apiStatusMessage = string.Empty;

    // --- Поля модального вікна категорії ---
    [ObservableProperty] private bool _isCategoryDialogOpen;
    [ObservableProperty] private string _newCategoryName = string.Empty;
    [ObservableProperty] private string _newCategoryDescription = string.Empty;

    // --- Поля модального вікна продукту ---
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

    // --- Поля модального вікна страви ---
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

    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "Назва (А-Я)",
        "Назва (Я-А)",
        "Калорійність (зростання)",
        "Калорійність (спадання)",
        "Білки (спадання)"
    };

    public CatalogViewModel(
        IProductService productService,
        IDishService dishService,
        ICategoryService categoryService,
        ISortService sortService,
        IUndoService undoService,
        IAuthenticationService authService,
        ILoggingService loggingService,
        IOpenFoodFactsService openFoodFactsService)
    {
        _productService = productService;
        _dishService = dishService;
        _categoryService = categoryService;
        _sortService = sortService;
        _undoService = undoService;
        _authService = authService;
        _loggingService = loggingService;
        _openFoodFactsService = openFoodFactsService;
    }

    [RelayCommand]
    private void OpenApiSearchDialog()
    {
        ApiSearchQuery = SearchQuery;
        ApiSearchResults.Clear();
        SelectedApiProduct = null;
        ApiStatusMessage = "Введіть назву продукту та натисніть «Шукати».";

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
        if (string.IsNullOrWhiteSpace(ApiSearchQuery))
        {
            ApiStatusMessage = "Введіть текст для пошуку.";
            return;
        }

        if (IsApiLoading) return;

        IsApiLoading = true;
        ApiSearchResults.Clear();
        SelectedApiProduct = null;
        ApiStatusMessage = "⏳ Виконується завантаження з Open Food Facts...";

        try
        {
            var results = await _openFoodFactsService.SearchAsync(ApiSearchQuery, cancellationToken);
            
            if (results.Count == 0)
            {
                ApiStatusMessage = "❌ Нічого не знайдено. Спробуйте уточнити назву.";
            }
            else
            {
                ApiSearchResults = new ObservableCollection<ExternalProductDto>(results);
                SelectedApiProduct = ApiSearchResults.FirstOrDefault();
                ApiStatusMessage = $"✅ Знайдено {ApiSearchResults.Count} продуктів.";
            }
        }
        catch (Exception ex)
        {
            ApiStatusMessage = $"⚠️ Помилка з'єднання: {ex.Message}";
        }
        finally
        {
            IsApiLoading = false;
        }
    }

    [RelayCommand]
    private async Task ImportApiProductAsync(System.Windows.Window? dialogWindow, CancellationToken cancellationToken = default)
    {
        if (SelectedApiProduct == null)
        {
            StatusMessage = "Оберіть продукт зі списку результатів API.";
            return;
        }

        var defaultCategory = Categories.FirstOrDefault(c => c.Name != "Усі категорії") ?? Categories.FirstOrDefault();
        if (defaultCategory == null)
        {
            StatusMessage = "Створіть хоча б одну категорію для імпорту продуктів.";
            return;
        }

        try
        {
            await _productService.CreateAsync(
                SelectedApiProduct.Name,
                defaultCategory.Id,
                $"Імпортовано з Open Food Facts (Код: {SelectedApiProduct.Code})",
                NutritionBasis.Per100Grams,
                100m,
                SelectedApiProduct.Calories,
                SelectedApiProduct.Proteins,
                SelectedApiProduct.Fats,
                SelectedApiProduct.Carbs,
                Array.Empty<string>(),
                Array.Empty<string>(),
                cancellationToken);

            StatusMessage = $"Продукт «{SelectedApiProduct.Name}» успішно додано в локальний каталог!";
            
            // Закриваємо вікно після успішного збереження
            dialogWindow?.Close();

            await ApplyFiltersAndSearchAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка збереження в каталог: {ex.Message}";
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

        var filterList = new List<Category> { AllCategoriesOption };
        filterList.AddRange(cats);
        FilterCategories = new ObservableCollection<Category>(filterList);

        SelectedCategory = AllCategoriesOption;
        await ApplyFiltersAndSearchAsync(cancellationToken);
    }

    [RelayCommand]
    public async Task ApplyFiltersAndSearchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var allProducts = await _productService.GetAllAsync(cancellationToken: cancellationToken);
            var productsQuery = allProducts.AsEnumerable();

            if (SelectedCategory != null && SelectedCategory != AllCategoriesOption && SelectedCategory.Id != Guid.Empty)
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
                CategoryName = p.Category?.Name ?? "Без категорії",
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

                if (SelectedCategory != null && SelectedCategory != AllCategoriesOption && SelectedCategory.Id != Guid.Empty)
                {
                    dishesQuery = dishesQuery.Where(d => d.CategoryId == SelectedCategory.Id);
                }

                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    var query = SearchQuery.Trim();
                    // Шукаємо як у назві страви, так і серед її інгредієнтів
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
                        CategoryName = d.Category?.Name ?? "Складена страва",
                        Calories = (double)nutrition.Calories,
                        Proteins = (double)nutrition.ProteinG,
                        Fats = (double)nutrition.FatG,
                        Carbs = (double)nutrition.CarbsG,
                        IsDish = true
                    });
                }
            }

            dtos = SelectedSortOption switch
            {
                "Назва (Я-А)" => dtos.OrderByDescending(x => x.Name).ToList(),
                "Калорійність (зростання)" => dtos.OrderBy(x => x.Calories).ThenBy(x => x.Name).ToList(),
                "Калорійність (спадання)" => dtos.OrderByDescending(x => x.Calories).ThenBy(x => x.Name).ToList(),
                "Білки (спадання)" => dtos.OrderByDescending(x => x.Proteins).ThenBy(x => x.Name).ToList(),
                _ => dtos.OrderBy(x => x.Name).ToList()
            };

            FilteredItems = new ObservableCollection<FoodItemDisplayDto>(dtos);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка завантаження товарів: {ex.Message}";
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
        if (string.IsNullOrWhiteSpace(NewCategoryName))
        {
            StatusMessage = "Введіть назву категорії.";
            return;
        }

        try
        {
            await _categoryService.CreateAsync(NewCategoryName, NewCategoryDescription, cancellationToken);
            if (_authService.CurrentUser != null)
            {
                await _loggingService.LogActionAsync(_authService.CurrentUser.Id, ActionType.Create, $"Створено нову категорію: «{NewCategoryName}».", cancellationToken: cancellationToken);
            }
            StatusMessage = $"Категорію «{NewCategoryName}» успішно створено.";
            IsCategoryDialogOpen = false;
            await LoadDataAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка створення категорії: {ex.Message}";
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
        if (SelectedItem == null || SelectedItem.IsDish)
        {
            StatusMessage = "Оберіть продукт для редагування (не страву).";
            return;
        }

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
        if (string.IsNullOrWhiteSpace(ProductName) || ProductSelectedCategory == null)
        {
            StatusMessage = "Заповніть назву та виберіть категорію.";
            return;
        }

        if (!decimal.TryParse(ProductReferenceAmount, out var refAmt) ||
            !decimal.TryParse(ProductCalories, out var cal) ||
            !decimal.TryParse(ProductProteins, out var prot) ||
            !decimal.TryParse(ProductFats, out var fat) ||
            !decimal.TryParse(ProductCarbs, out var carbs))
        {
            StatusMessage = "Перевірте коректність числових полів КБЖУ.";
            return;
        }

        var allergens = ProductAllergens.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tags = ProductDietaryTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            var userId = _authService.CurrentUser?.Id;
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
                if (userId != null)
                {
                    await _loggingService.LogActionAsync(userId, ActionType.Update, $"Оновлено продукт: «{ProductName}».", cancellationToken: cancellationToken);
                }
                StatusMessage = $"Продукт «{ProductName}» успішно оновлено.";
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
                if (userId != null)
                {
                    await _loggingService.LogActionAsync(userId, ActionType.Create, $"Створено новий продукт: «{ProductName}».", cancellationToken: cancellationToken);
                }
                StatusMessage = $"Продукт «{ProductName}» успішно створено.";
            }

            IsProductDialogOpen = false;
            await ApplyFiltersAndSearchAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка збереження: {ex.Message}";
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
        if (SelectedItem == null || !SelectedItem.IsDish)
        {
            StatusMessage = "Оберіть страву для редагування.";
            return;
        }

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
                ProductName = ing.Product?.Name ?? "Продукт",
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
        if (!decimal.TryParse(IngredientAmount, out var amt) || amt <= 0)
        {
            StatusMessage = "Вкажіть масу інгредієнта > 0.";
            return;
        }

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
        if (string.IsNullOrWhiteSpace(DishName) || DishSelectedCategory == null)
        {
            StatusMessage = "Заповніть назву та виберіть категорію для страви.";
            return;
        }

        if (DishIngredients.Count == 0)
        {
            StatusMessage = "Додайте хоча б один інгредієнт до страви.";
            return;
        }

        var inputs = DishIngredients.Select(i => new DishIngredientInput(i.ProductId, i.Amount, i.Unit));

        try
        {
            var userId = _authService.CurrentUser?.Id;
            if (IsEditingDish && EditingDishId.HasValue)
            {
                await _dishService.UpdateAsync(
                    EditingDishId.Value,
                    DishName,
                    DishSelectedCategory.Id,
                    DishDescription,
                    inputs,
                    cancellationToken);
                StatusMessage = $"Страву «{DishName}» успішно оновлено.";
                if (userId != null)
                {
                    await _loggingService.LogActionAsync(userId, ActionType.Update, $"Оновлено страву: «{DishName}».", cancellationToken: cancellationToken);
                }
            }
            else
            {
                await _dishService.CreateAsync(
                    DishName,
                    DishSelectedCategory.Id,
                    DishDescription,
                    inputs,
                    cancellationToken);
                StatusMessage = $"Страву «{DishName}» успішно створено.";
                if (userId != null)
                {
                    await _loggingService.LogActionAsync(userId, ActionType.Create, $"Створено нову страву: «{DishName}».", cancellationToken: cancellationToken);
                }
            }

            IsDishDialogOpen = false;
            await ApplyFiltersAndSearchAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка збереження страви: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DeleteSelectedItemAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedItem == null) return;

        var userId = _authService.CurrentUser?.Id;
        if (SelectedItem.IsDish)
        {
            await _dishService.DeleteAsync(SelectedItem.Id, cancellationToken);
            StatusMessage = $"Видалено страву «{SelectedItem.Name}».";
            if (userId != null)
            {
                await _loggingService.LogActionAsync(userId, ActionType.Delete, $"Видалено з каталогу: «{SelectedItem.Name}».", cancellationToken: cancellationToken);
            }
        }
        else
        {
            await _productService.DeleteAsync(SelectedItem.Id, cancellationToken);
            StatusMessage = $"Видалено продукт «{SelectedItem.Name}».";
            if (userId != null)
            {
                await _loggingService.LogActionAsync(userId, ActionType.Delete, $"Видалено з каталогу: «{SelectedItem.Name}».", cancellationToken: cancellationToken);
            }
        }

        await ApplyFiltersAndSearchAsync(cancellationToken);
    }
}