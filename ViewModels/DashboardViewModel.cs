using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public class TodayPlanItemDisplayDto : ObservableObject
{
    public Guid PlanItemId { get; set; }
    public string MealName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public double PortionGrams { get; set; }
    public double Calories { get; set; }
    public double Proteins { get; set; }
    public double Fats { get; set; }
    public double Carbs { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? DishId { get; set; }

    private bool _isEaten;
    public bool IsEaten
    {
        get => _isEaten;
        set => SetProperty(ref _isEaten, value);
    }
}

public partial class DashboardViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly IAuthenticationService _auth;
    private readonly INutritionCalculator _calculator;
    private readonly IUndoService _undoService;
    private readonly IMealIntakeService _mealIntakeService;
    private readonly IReportService _reportService;
    private readonly IProductService _productService;
    private readonly IDishService _dishService;
    private readonly IPlanService _planService;
    private readonly ILoggingService _loggingService;
    private readonly IOpenFoodFactsService _openFoodFactsService;

    // Кеш для уникнення повторення рекомендованих перекусів
    private readonly HashSet<Guid> _excludedSuggestedIds = new();
    private readonly HashSet<string> _shownSuggestedNames = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;
    
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty] private string _welcomeText = string.Empty;
    [ObservableProperty] private bool _isEditIntakeDialogOpen;
    [ObservableProperty] private MealIntakeDisplayDto? _selectedIntakeToEdit;
    [ObservableProperty] private string _editIntakeGramsInput = "100";

    [ObservableProperty] private double _currentCalories = 0;
    [ObservableProperty] private double _targetCalories = 2000;
    [ObservableProperty] private double _currentProteins = 0;
    [ObservableProperty] private double _targetProteins = 150;
    [ObservableProperty] private double _currentFats = 0;
    [ObservableProperty] private double _targetFats = 65;
    [ObservableProperty] private double _currentCarbs = 0;
    [ObservableProperty] private double _targetCarbs = 200;
    [ObservableProperty] private string _caloriesDisplay = "0 / 2000 ккал";
    [ObservableProperty] private string _proteinsDisplay = "0 / 150 г";
    [ObservableProperty] private string _fatsDisplay = "0 / 65 г";
    [ObservableProperty] private string _carbsDisplay = "0 / 200 г";

    // --- ПЕРЕВИЩЕННЯ КБЖУ ТА ВОДИ ---
    [ObservableProperty] private bool _isCaloriesExceeded;
    [ObservableProperty] private bool _isFatsExceeded;
    [ObservableProperty] private bool _isCarbsExceeded;
    [ObservableProperty] private bool _isProteinsExceeded;
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(HasMacroWarning))]
    private string _macroWarningMessage = string.Empty;

    public bool HasMacroWarning => !string.IsNullOrWhiteSpace(MacroWarningMessage);

    [ObservableProperty] private bool _isAddMealDialogOpen;
    [ObservableProperty] private ObservableCollection<FoodItemDisplayDto> _availableFoodItems = new();
    [ObservableProperty] private FoodItemDisplayDto? _selectedFoodItem;
    [ObservableProperty] private string _portionGramsInput = "100";

    [ObservableProperty] private ObservableCollection<MealIntakeDisplayDto> _todayIntakes = new();
    [ObservableProperty] private ObservableCollection<TodayPlanItemDisplayDto> _todayPlanItems = new();
    public ObservableCollection<TodayPlanItemDisplayDto> DisplayPlanItems => TodayPlanItems;

    [ObservableProperty] private bool _isSuggestMealDialogOpen;
    [ObservableProperty] private ObservableCollection<FoodItemDisplayDto> _suggestedMealOptions = new();
    [ObservableProperty] private FoodItemDisplayDto? _selectedSuggestedMeal;
    [ObservableProperty] private string _suggestedPortionGramsInput = "100";
    
    // --- ПЛАН ТА РАЦІОНИ ---
    [ObservableProperty] private ObservableCollection<NutritionPlan> _availablePlans = new();
    [ObservableProperty] private NutritionPlan? _selectedPlan;
    [ObservableProperty] private NutritionPlan? _currentPlan;

    // --- СТАН ФАКТИЧНО З'ЇДЕНОГО ---
    [ObservableProperty] private bool _hasEatenToday;
    [ObservableProperty] private bool _hasNoEatenToday = true;

    // --- ЛІЧИЛЬНИК ВОДИ ---
    [ObservableProperty] private double _waterDrankLiters = 0.0;
    [ObservableProperty] private double _waterTargetLiters = 2.0;
    [ObservableProperty] private bool _isEditWaterTargetDialogOpen;
    [ObservableProperty] private string _waterTargetInput = "2.0";
    
    public DashboardViewModel(
        INavigationService navigation,
        IAuthenticationService auth,
        INutritionCalculator calculator,
        IUndoService undoService,
        IMealIntakeService mealIntakeService,
        IReportService reportService,
        IProductService productService,
        IDishService dishService,
        IPlanService planService,
        ILoggingService loggingService,
        IOpenFoodFactsService openFoodFactsService)
    {
        _navigation = navigation;
        _auth = auth;
        _calculator = calculator;
        _undoService = undoService;
        _mealIntakeService = mealIntakeService;
        _reportService = reportService;
        _productService = productService;
        _dishService = dishService;
        _planService = planService;
        _loggingService = loggingService;
        _openFoodFactsService = openFoodFactsService;
    }
    
    partial void OnSelectedPlanChanged(NutritionPlan? value)
    {
        CurrentPlan = value;
        UpdateTodayPlanDisplay(value);
    }

    private void UpdateTodayPlanDisplay(NutritionPlan? plan)
    {
        if (plan == null)
        {
            TodayPlanItems = new ObservableCollection<TodayPlanItemDisplayDto>();
            OnPropertyChanged(nameof(DisplayPlanItems));
            return;
        }

        var intakes = TodayIntakes ?? new ObservableCollection<MealIntakeDisplayDto>();
        var planDtos = new List<TodayPlanItemDisplayDto>();

        if (plan.Items != null)
        {
            foreach (var item in plan.Items)
            {
                var itemName = item.Product?.Name ?? item.Dish?.Name ?? item.MealName;
                
                bool isAlreadyEaten = intakes.Any(i => 
                    (item.ProductId.HasValue && i.ProductId == item.ProductId) ||
                    (item.DishId.HasValue && i.DishId == item.DishId) ||
                    i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase));

                planDtos.Add(new TodayPlanItemDisplayDto
                {
                    PlanItemId = item.Id,
                    MealName = item.MealName,
                    ItemName = itemName,
                    PortionGrams = (double)item.PortionAmount,
                    Calories = (double)item.Calories,
                    Proteins = (double)item.ProteinG,
                    Fats = (double)item.FatG,
                    Carbs = (double)item.CarbsG,
                    ProductId = item.ProductId,
                    DishId = item.DishId,
                    IsEaten = isAlreadyEaten
                });
            }
        }

        TodayPlanItems = new ObservableCollection<TodayPlanItemDisplayDto>(planDtos);
        OnPropertyChanged(nameof(DisplayPlanItems));
    }

    public async Task LoadDashboardDataAsync()
    {
        var user = _auth.CurrentUser;
        if (user == null) return;

        WelcomeText = $"Вітаємо, {user.DisplayName}!";

        // --- 1. АВТОМАТИЧНА СИНХРОНІЗАЦІЯ ІСТОРІЇ ПРИ ЗМІНІ ДНЯ ТА ЗАПУСКУ ---
        try
        {
            await _mealIntakeService.SyncPastDaysEatenItemsAsync(user.Id);
        }
        catch { /* Ігноруємо помилки мережі/бази при старті */ }

        try
        {
            var calc = _calculator.Calculate(user);
            TargetCalories = (double)calc.Targets.Calories;
            TargetProteins = (double)calc.Targets.ProteinG;
            TargetFats = (double)calc.Targets.FatG;
            TargetCarbs = (double)calc.Targets.CarbsG;
        }
        catch
        {
            TargetCalories = 2000;
            TargetProteins = 150;
            TargetFats = 65;
            TargetCarbs = 200;
        }

        var intakes = await _mealIntakeService.GetTodayIntakesAsync(user.Id);
        TodayIntakes = new ObservableCollection<MealIntakeDisplayDto>(intakes);
        HasEatenToday = TodayIntakes.Count > 0;
        HasNoEatenToday = !HasEatenToday;

        CurrentCalories = intakes.Sum(x => x.Calories);
        CurrentProteins = intakes.Sum(x => x.Proteins);
        CurrentFats = intakes.Sum(x => x.Fats);
        CurrentCarbs = intakes.Sum(x => x.Carbs);

        CaloriesDisplay = $"{CurrentCalories:F0} / {TargetCalories:F0} ккал";
        ProteinsDisplay = $"{CurrentProteins:F1} / {TargetProteins:F1} г";
        FatsDisplay = $"{CurrentFats:F1} / {TargetFats:F1} г";
        CarbsDisplay = $"{CurrentCarbs:F1} / {TargetCarbs:F1} г";

        var currentSelectedId = SelectedPlan?.Id;

        var history = await _planService.GetHistoryAsync(user.Id);
        AvailablePlans = new ObservableCollection<NutritionPlan>(history);

        var todayPlan = await _planService.GetForDateAsync(user.Id, DateTime.UtcNow);

        if (currentSelectedId.HasValue && AvailablePlans.Any(p => p.Id == currentSelectedId.Value))
        {
            SelectedPlan = AvailablePlans.First(p => p.Id == currentSelectedId.Value);
        }
        else
        {
            SelectedPlan = todayPlan ?? AvailablePlans.FirstOrDefault();
        }

        UpdateTodayPlanDisplay(SelectedPlan);

        LoadWaterData(user.Id);
        CheckMacroExceedance();
    }

    private void CheckMacroExceedance()
    {
        IsCaloriesExceeded = CurrentCalories > TargetCalories;
        IsFatsExceeded = CurrentFats > TargetFats;
        IsCarbsExceeded = CurrentCarbs > TargetCarbs;

        var warnings = new List<string>();

        if (IsFatsExceeded)
        {
            var diff = CurrentFats - TargetFats;
            warnings.Add($"жирів (+{diff:F1}г)");
        }
        if (IsCaloriesExceeded)
        {
            var diff = CurrentCalories - TargetCalories;
            warnings.Add($"калорій (+{diff:F0}ккал)");
        }
        if (IsCarbsExceeded)
        {
            var diff = CurrentCarbs - TargetCarbs;
            warnings.Add($"вуглеводів (+{diff:F1}г)");
        }
        if (WaterDrankLiters > WaterTargetLiters + 0.5)
        {
            var diffWater = WaterDrankLiters - WaterTargetLiters;
            warnings.Add($"води (+{diffWater:F2}Л)");
        }

        if (warnings.Count > 0)
        {
            MacroWarningMessage = $"💡 Ви трохи орієнтовно перевищили ціль: {string.Join(", ", warnings)}. Це цілком нормально в межах тижневого балансу! За бажанням скоригуйте наступні прийоми їжі.";
        }
        else
        {
            MacroWarningMessage = string.Empty;
        }
    }

    private string GetWaterDirectory()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DietPlanner");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void LoadWaterData(Guid userId)
    {
        try
        {
            var dir = GetWaterDirectory();
            var targetFile = Path.Combine(dir, $"watertarget_{userId}.txt");
            if (File.Exists(targetFile) && double.TryParse(File.ReadAllText(targetFile), out var target))
            {
                WaterTargetLiters = target;
            }

            var drankFile = Path.Combine(dir, $"water_{userId}_{DateTime.UtcNow:yyyyMMdd}.txt");
            if (File.Exists(drankFile) && double.TryParse(File.ReadAllText(drankFile), out var drank))
            {
                WaterDrankLiters = drank;
            }
            else
            {
                WaterDrankLiters = 0.0;
            }
        }
        catch { }
    }

    private void SaveWaterData(Guid userId)
    {
        try
        {
            var dir = GetWaterDirectory();
            var targetFile = Path.Combine(dir, $"watertarget_{userId}.txt");
            File.WriteAllText(targetFile, WaterTargetLiters.ToString("F2"));

            var drankFile = Path.Combine(dir, $"water_{userId}_{DateTime.UtcNow:yyyyMMdd}.txt");
            File.WriteAllText(drankFile, WaterDrankLiters.ToString("F2"));
        }
        catch { }
    }

    [RelayCommand]
    private async Task DeleteIntakeAsync(MealIntakeDisplayDto? intake)
    {
        if (intake == null) return;

        var user = _auth.CurrentUser;
        if (user == null) return;

        try
        {
            await _mealIntakeService.RemoveIntakeItemByFoodAsync(user.Id, intake.ProductId, intake.DishId, intake.ItemName);
            StatusMessage = $"🗑️ Видалено з прийомів їжі: {intake.ItemName}.";
            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка видалення: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddWater(string amountStr)
    {
        if (double.TryParse(amountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount))
        {
            WaterDrankLiters = Math.Round(WaterDrankLiters + amount, 2);
            var user = _auth.CurrentUser;
            if (user != null) SaveWaterData(user.Id);

            CheckMacroExceedance();

            if (WaterDrankLiters > 4.0)
            {
                StatusMessage = $"⚠️ Увага! Випито {WaterDrankLiters:F2} Л води (надмірна кількість).";
            }
            else
            {
                StatusMessage = $"💧 Додано {amount * 1000:F0} мл води. Всього випито: {WaterDrankLiters:F2} Л.";
            }
        }
    }

    [RelayCommand]
    private void ResetWater()
    {
        WaterDrankLiters = 0.0;
        var user = _auth.CurrentUser;
        if (user != null) SaveWaterData(user.Id);
        CheckMacroExceedance();
        StatusMessage = "💧 Лічильник випитої води скинуто.";
    }

    [RelayCommand]
    private void OpenEditWaterTargetDialog()
    {
        WaterTargetInput = WaterTargetLiters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        IsEditWaterTargetDialogOpen = true;
    }

    [RelayCommand]
    private void CloseEditWaterTargetDialog() => IsEditWaterTargetDialogOpen = false;

    [RelayCommand]
    private void SaveWaterTarget()
    {
        if (double.TryParse(WaterTargetInput.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var newTarget) && newTarget > 0)
        {
            WaterTargetLiters = Math.Round(newTarget, 1);
            IsEditWaterTargetDialogOpen = false;
            var user = _auth.CurrentUser;
            if (user != null) SaveWaterData(user.Id);
            CheckMacroExceedance();
            StatusMessage = $"Добову норму води оновлено до {WaterTargetLiters:F1} Л.";
        }
        else
        {
            StatusMessage = "Вкажіть коректну кількість літрів (більше 0).";
        }
    }

    // --- ПОШУК ПЕРЕКУСІВ ЧЕРЕЗ API ЗА ПОШУКОВИМИ ТЕГАМИ УКРАЇНСЬКОЮ ТА АНГЛІЙСЬКОЮ ---
    [RelayCommand]
    private async Task SuggestMealAsync()
    {
        var user = _auth.CurrentUser;
        if (user == null) return;

        StatusMessage = "🔍 Підбираємо перекуси з бази даних та OpenFoodFacts API...";

        // 1. Локальні варіанти з БД
        var localOptions = await _mealIntakeService.SuggestMealOptionsAsync(user.Id, limit: 15, excludeIds: _excludedSuggestedIds);
        localOptions ??= new List<FoodItemDisplayDto>();

        localOptions = localOptions.Where(o => !_shownSuggestedNames.Contains(o.Name)).ToList();

        // 2. Запити до API за україномовними та загальними категоріями перекусів
        var apiOptions = new List<FoodItemDisplayDto>();
        try
        {
            var searchTerms = new[] { "перекус", "снек", "горіхи", "печиво", "сухофрукти", "snack" };
            
            foreach (var term in searchTerms)
            {
                var apiProducts = await _openFoodFactsService.SearchAsync(term, cancellationToken: CancellationToken.None);
                if (apiProducts != null && apiProducts.Count > 0)
                {
                    foreach (var p in apiProducts)
                    {
                        if (string.IsNullOrWhiteSpace(p.Name) || _shownSuggestedNames.Contains(p.Name)) 
                            continue;

                        apiOptions.Add(new FoodItemDisplayDto
                        {
                            Id = Guid.NewGuid(),
                            Name = $"🌐 {p.Name}",
                            CategoryName = string.IsNullOrWhiteSpace(p.BrandOrCategory) ? "Перекус (OpenFoodFacts API)" : p.BrandOrCategory,
                            Calories = (double)p.Calories,
                            Proteins = (double)p.Proteins,
                            Fats = (double)p.Fats,
                            Carbs = (double)p.Carbs,
                            IsDish = false
                        });
                    }
                }

                if (apiOptions.Count >= 20) break;
            }
        }
        catch
        {
            // При відсутності мережі продовжуємо з локальною БД
        }

        // 3. Об'єднання
        var combinedList = new List<FoodItemDisplayDto>();
        combinedList.AddRange(localOptions);
        combinedList.AddRange(apiOptions);

        if (IsFatsExceeded || IsCaloriesExceeded)
        {
            combinedList = combinedList.Where(o => o.Fats <= 8.0).ToList();
        }

        var random = new Random();
        var finalSelection = combinedList.OrderBy(_ => random.Next()).Take(8).ToList();

        if (finalSelection.Count > 0)
        {
            foreach (var item in finalSelection)
            {
                _excludedSuggestedIds.Add(item.Id);
                _shownSuggestedNames.Add(item.Name);
            }

            if (_shownSuggestedNames.Count > 80)
            {
                _shownSuggestedNames.Clear();
            }

            SuggestedMealOptions = new ObservableCollection<FoodItemDisplayDto>(finalSelection);
            SelectedSuggestedMeal = SuggestedMealOptions.FirstOrDefault();
            IsSuggestMealDialogOpen = true;
            StatusMessage = string.Empty;
        }
        else
        {
            _shownSuggestedNames.Clear();
            _excludedSuggestedIds.Clear();
            
            var fallbackProducts = await _productService.GetAllAsync();
            var fallbackList = fallbackProducts.Select(p => new FoodItemDisplayDto
            {
                Id = p.Id,
                Name = p.Name,
                CategoryName = p.Category?.Name ?? "Перекус",
                Calories = (double)p.Calories,
                Proteins = (double)p.ProteinG,
                Fats = (double)p.FatG,
                Carbs = (double)p.CarbsG,
                IsDish = false
            }).Take(5).ToList();

            if (fallbackList.Count > 0)
            {
                SuggestedMealOptions = new ObservableCollection<FoodItemDisplayDto>(fallbackList);
                SelectedSuggestedMeal = SuggestedMealOptions.FirstOrDefault();
                IsSuggestMealDialogOpen = true;
                StatusMessage = string.Empty;
            }
            else
            {
                StatusMessage = "Перекусів не знайдено.";
            }
        }
    }

    [RelayCommand]
    private void CloseSuggestMealDialog() => IsSuggestMealDialogOpen = false;

    [RelayCommand]
    private async Task ConfirmSuggestMealAsync()
    {
        if (SelectedSuggestedMeal == null)
        {
            StatusMessage = "Оберіть варіант із запропонованого списку.";
            return;
        }

        if (!decimal.TryParse(SuggestedPortionGramsInput, out var amountGrams) || amountGrams <= 0)
        {
            StatusMessage = "Вкажіть масу у грамах більше 0.";
            return;
        }

        var user = _auth.CurrentUser;
        if (user == null) return;

        Guid? productId = SelectedSuggestedMeal.IsDish ? null : SelectedSuggestedMeal.Id;
        Guid? dishId = SelectedSuggestedMeal.IsDish ? SelectedSuggestedMeal.Id : null;

        if (!SelectedSuggestedMeal.IsDish && productId.HasValue)
        {
            var cleanName = SelectedSuggestedMeal.Name.Replace("🌐 ", "").Trim();
            var products = await _productService.GetAllAsync();
            var existingProduct = products.FirstOrDefault(p => p.Id == productId.Value || p.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase));

            if (existingProduct == null)
            {
                var defaultCategoryId = products.FirstOrDefault()?.CategoryId ?? Guid.Empty;

                if (defaultCategoryId != Guid.Empty)
                {
                    var newProduct = await _productService.CreateAsync(
                        cleanName,
                        defaultCategoryId,
                        "Перекус із OpenFoodFacts API",
                        NutritionBasis.Per100Grams,
                        100m,
                        (decimal)SelectedSuggestedMeal.Calories,
                        (decimal)SelectedSuggestedMeal.Proteins,
                        (decimal)SelectedSuggestedMeal.Fats,
                        (decimal)SelectedSuggestedMeal.Carbs,
                        Array.Empty<string>(),
                        Array.Empty<string>());

                    productId = newProduct.Id;
                }
            }
            else
            {
                productId = existingProduct.Id;
            }
        }

        await _mealIntakeService.AddIntakeItemAsync(user.Id, productId, dishId, amountGrams);

        IsSuggestMealDialogOpen = false;
        StatusMessage = $"Зараховано в прийоми їжі: {SelectedSuggestedMeal.Name} ({amountGrams:F0}г).";
        await LoadDashboardDataAsync();
    }

    [RelayCommand]
    private async Task RefreshSuggestedMealsAsync()
    {
        await SuggestMealAsync();
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var user = _auth.CurrentUser;
        if (user == null) return;

        try
        {
            var filePath = await _reportService.GenerateUserReportAsync(user.Id);
            if (!string.IsNullOrEmpty(filePath))
            {
                StatusMessage = $"📄 Звіт успішно збережено у файл: {filePath}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка експорту: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        var user = _auth.CurrentUser;
        if (user == null) return;

        try
        {
            await _undoService.UndoLastActionAsync();
            StatusMessage = "↩️ Останню дію успішно скасовано.";
            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не вдалося скасувати дію: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task MarkPlanItemAsEatenAsync(TodayPlanItemDisplayDto? item)
    {
        if (item == null || item.IsEaten) return;

        var user = _auth.CurrentUser;
        if (user == null) return;

        try
        {
            await _mealIntakeService.AddIntakeItemAsync(user.Id, item.ProductId, item.DishId, (decimal)item.PortionGrams);
            item.IsEaten = true;
            StatusMessage = $"Зараховано в прийоми їжі: {item.ItemName} ({item.PortionGrams:F0}г).";
            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка збереження: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddMealAsync()
    {
        var products = await _productService.GetAllAsync();
        var items = products.Select(p => new FoodItemDisplayDto
        {
            Id = p.Id,
            Name = $"{p.Name} ({p.Calories:F0} ккал/100г)",
            CategoryName = p.Category?.Name ?? "Продукт",
            Calories = (double)p.Calories,
            Proteins = (double)p.ProteinG,
            Fats = (double)p.FatG,
            Carbs = (double)p.CarbsG,
            IsDish = false
        }).ToList();

        var dishes = await _dishService.GetAllAsync();
        foreach (var d in dishes)
        {
            var nutrition = d.CalculateNutrition();
            items.Add(new FoodItemDisplayDto
            {
                Id = d.Id,
                Name = $"[Страва] {d.Name} ({nutrition.Calories:F0} ккал/100г)",
                CategoryName = d.Category?.Name ?? "Страва",
                Calories = (double)nutrition.Calories,
                Proteins = (double)nutrition.ProteinG,
                Fats = (double)nutrition.FatG,
                Carbs = (double)nutrition.CarbsG,
                IsDish = true
            });
        }

        AvailableFoodItems = new ObservableCollection<FoodItemDisplayDto>(items);
        if (AvailableFoodItems.Count > 0)
        {
            SelectedFoodItem = AvailableFoodItems[0];
        }

        IsAddMealDialogOpen = true;
    }

    [RelayCommand]
    private async Task SaveMealIntakeAsync()
    {
        if (SelectedFoodItem == null)
        {
            StatusMessage = "Оберіть продукт або страву зі списку.";
            return;
        }

        if (!decimal.TryParse(PortionGramsInput, out var amountGrams) || amountGrams <= 0)
        {
            StatusMessage = "Вкажіть коректну масу у грамах (більше 0).";
            return;
        }

        var user = _auth.CurrentUser;
        if (user == null) return;

        Guid? productId = SelectedFoodItem.IsDish ? null : SelectedFoodItem.Id;
        Guid? dishId = SelectedFoodItem.IsDish ? SelectedFoodItem.Id : null;

        await _mealIntakeService.AddIntakeItemAsync(user.Id, productId, dishId, amountGrams);

        IsAddMealDialogOpen = false;
        StatusMessage = $"Додано прийом їжі: {SelectedFoodItem.Name} ({amountGrams}г).";
        await LoadDashboardDataAsync();
    }

    [RelayCommand]
    private void CloseAddMealDialog() => IsAddMealDialogOpen = false;

    [RelayCommand]
    private async Task TogglePlanItemEatenAsync(TodayPlanItemDisplayDto? item)
    {
        if (item == null) return;

        var user = _auth.CurrentUser;
        if (user == null) return;

        try
        {
            if (!item.IsEaten)
            {
                await _mealIntakeService.AddIntakeItemAsync(user.Id, item.ProductId, item.DishId, (decimal)item.PortionGrams);
                StatusMessage = $"Зараховано в прийоми їжі: {item.ItemName} ({item.PortionGrams:F0}г).";
            }
            else
            {
                await _mealIntakeService.RemoveIntakeItemByFoodAsync(user.Id, item.ProductId, item.DishId, item.ItemName);
                StatusMessage = $"Скасовано позначку для: {item.ItemName}.";
            }

            await LoadDashboardDataAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка збереження: {ex.Message}";
        }
    }
}