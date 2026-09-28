using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public class MealSlotDisplayDto
{
    public Guid PlanItemId { get; set; }
    public string SlotName { get; set; } = string.Empty;
    public string SuggestedItemName { get; set; } = string.Empty;
    public double PortionGrams { get; set; }
    public double Calories { get; set; }
    public double Proteins { get; set; }
    public double Fats { get; set; }
    public double Carbs { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? DishId { get; set; }
}

public partial class PlanGeneratorViewModel : ViewModelBase
{
    private readonly IPlanService _planService;
    private readonly IAuthenticationService _authService;
    private readonly INutritionCalculator _calculator;
    private readonly IProductService _productService;
    private readonly IDishService _dishService;

    [ObservableProperty] private int _mealCount = 4;
    
    // Діапазон калорій із профілю (ручне введення виключено)
    [ObservableProperty] private decimal _calculatedTargetCalories = 2000m;
    [ObservableProperty] private string _calorieRangeText = "1900 – 2100 ккал/день";

    [ObservableProperty] private ObservableCollection<MealSlotDisplayDto> _generatedSlots = new();
    [ObservableProperty] private ObservableCollection<NutritionPlan> _planHistory = new();
    [ObservableProperty] private NutritionPlan? _selectedHistoryPlan;

    [ObservableProperty] private string _generationResultSummary = string.Empty;
    [ObservableProperty] private string _userSummaryInfo = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // Модальне вікно заміни
    [ObservableProperty] private bool _isReplaceDialogOpen;
    [ObservableProperty] private MealSlotDisplayDto? _selectedSlotToReplace;
    [ObservableProperty] private ObservableCollection<FoodItemDisplayDto> _availableReplacementItems = new();
    [ObservableProperty] private FoodItemDisplayDto? _selectedReplacementItem;
    [ObservableProperty] private string _replacePortionGramsInput = "100";

    public PlanGeneratorViewModel(
        IPlanService planService,
        IAuthenticationService authService,
        INutritionCalculator calculator,
        IProductService productService,
        IDishService dishService)
    {
        _planService = planService;
        _authService = authService;
        _calculator = calculator;
        _productService = productService;
        _dishService = dishService;

        LoadUserProfileAndTargets();
    }

    public void LoadUserProfileAndTargets()
    {
        var user = _authService.CurrentUser;
        if (user == null)
        {
            UserSummaryInfo = "Користувач не авторизований.";
            return;
        }

        if (user.WeightKg == null || user.HeightCm == null || user.SexForCalculation == null)
        {
            UserSummaryInfo = "⚠️ Увага: У профілі не заповнено вагу, зріст або стать. Заповніть їх у меню «Профіль».";
            return;
        }

        try
        {
            var calcResult = _calculator.Calculate(user);
            CalculatedTargetCalories = calcResult.Targets.Calories;

            decimal minCal = Math.Round(CalculatedTargetCalories * 0.95m);
            decimal maxCal = Math.Round(CalculatedTargetCalories * 1.05m);
            CalorieRangeText = $"{minCal:F0} – {maxCal:F0} ккал/день";

            string goalStr = user.Goal switch
            {
                NutritionGoal.LoseWeight => "Схуднення",
                NutritionGoal.GainWeight => "Набір маси (накачатись)",
                NutritionGoal.MaintainWeight => "Підтримка ваги",
                NutritionGoal.Recompose => "Рекомпозиція тіла",
                _ => "Підтримка ваги"
            };

            string activityStr = user.ActivityLevel switch
            {
                ActivityLevel.Sedentary => "Низька (сидячий спосіб)",
                ActivityLevel.Light => "Легка активність",
                ActivityLevel.Moderate => "Помірні тренування",
                ActivityLevel.High => "Висока активність",
                ActivityLevel.VeryHigh => "Екстремальні навантаження",
                _ => "Помірна"
            };

            UserSummaryInfo = $"👤 {user.DisplayName} | 🎯 Ціль: {goalStr}\n" +
                              $"⚖️ Вага: {user.WeightKg} кг | 📏 Зріст: {user.HeightCm} см | 🏃 Активність: {activityStr}\n" +
                              $"📊 Добова норма КБЖУ: Б: {calcResult.Targets.ProteinG:F0}г | Ж: {calcResult.Targets.FatG:F0}г | В: {calcResult.Targets.CarbsG:F0}г";
        }
        catch (Exception ex)
        {
            UserSummaryInfo = $"Помилка розрахунку норми профілю: {ex.Message}";
        }
    }

    public async Task LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        var user = _authService.CurrentUser;
        if (user == null) return;

        var history = await _planService.GetHistoryAsync(user.Id, cancellationToken);
        PlanHistory = new ObservableCollection<NutritionPlan>(history);

        if (PlanHistory.Count > 0 && SelectedHistoryPlan == null)
        {
            SelectedHistoryPlan = PlanHistory[0];
        }
    }

    partial void OnSelectedHistoryPlanChanged(NutritionPlan? value)
    {
        if (value == null) return;

        GeneratedSlots.Clear();
        foreach (var item in value.Items)
        {
            GeneratedSlots.Add(new MealSlotDisplayDto
            {
                PlanItemId = item.Id,
                SlotName = item.MealName,
                SuggestedItemName = item.Dish != null ? $"[Страва] {item.Dish.Name}" : (item.Product?.Name ?? item.MealName),
                PortionGrams = (double)item.PortionAmount,
                Calories = (double)item.Calories,
                Proteins = (double)item.ProteinG,
                Fats = (double)item.FatG,
                Carbs = (double)item.CarbsG,
                ProductId = item.ProductId,
                DishId = item.DishId
            });
        }

        GenerationResultSummary = $"Завантажено раціон від {value.PlanDate:dd.MM.yyyy} ({value.TargetCalories:F0} ккал).";
    }

    // --- КНОПКА: ЗГЕНЕРУАТИ НА ДЕНЬ ---
    [RelayCommand]
    public async Task GenerateDayPlanAsync(CancellationToken cancellationToken = default)
    {
        var user = _authService.CurrentUser;
        if (user == null) return;

        StatusMessage = "⚡ Генеруємо оптимальний раціон на день...";
        
        var result = await _planService.GenerateAsync(user.Id, MealCount, CalculatedTargetCalories, cancellationToken);

        StatusMessage = result.Message;
        GenerationResultSummary = result.Message;

        if (!result.Success || result.Plan == null) return;

        await LoadHistoryAsync(cancellationToken);
        SelectedHistoryPlan = PlanHistory.FirstOrDefault(p => p.Id == result.Plan.Id);
    }

    // --- КНОПКА: ЗГЕНЕРУАТИ НА ТИЖДЕНЬ (7 ДНІВ) ---
    [RelayCommand]
    public async Task GenerateWeekPlanAsync(CancellationToken cancellationToken = default)
    {
        var user = _authService.CurrentUser;
        if (user == null) return;

        StatusMessage = "📅 Генеруємо різноманітне меню на 7 днів...";

        var results = await _planService.GenerateWeekAsync(user.Id, MealCount, CalculatedTargetCalories, cancellationToken);

        await LoadHistoryAsync(cancellationToken);

        if (results.Count > 0 && results[0].Plan != null)
        {
            SelectedHistoryPlan = PlanHistory.FirstOrDefault(p => p.Id == results[0].Plan!.Id);
            StatusMessage = $"🎉 Успішно створено меню на 7 днів! Оберіть необхідну дату зі списку нижче.";
            GenerationResultSummary = $"Сформовано 7 унікальних раціонів тижня.";
        }
    }

    [RelayCommand]
    public async Task OpenReplaceDialogAsync(MealSlotDisplayDto? slot, CancellationToken cancellationToken = default)
    {
        if (slot == null) return;

        SelectedSlotToReplace = slot;
        ReplacePortionGramsInput = slot.PortionGrams.ToString("0.##");

        var items = new List<FoodItemDisplayDto>();

        var dishes = await _dishService.GetAllAsync(cancellationToken: cancellationToken);
        foreach (var d in dishes)
        {
            var nutr = d.CalculateNutrition();
            items.Add(new FoodItemDisplayDto
            {
                Id = d.Id,
                Name = $"[Страва] {d.Name} ({nutr.Calories:F0} ккал/100г)",
                CategoryName = d.Category?.Name ?? "Страва",
                Calories = (double)nutr.Calories,
                Proteins = (double)nutr.ProteinG,
                Fats = (double)nutr.FatG,
                Carbs = (double)nutr.CarbsG,
                IsDish = true
            });
        }

        var products = await _productService.GetAllAsync(cancellationToken: cancellationToken);
        foreach (var p in products)
        {
            items.Add(new FoodItemDisplayDto
            {
                Id = p.Id,
                Name = $"{p.Name} ({p.Calories:F0} ккал/100г)",
                CategoryName = p.Category?.Name ?? "Продукт",
                Calories = (double)p.Calories,
                Proteins = (double)p.ProteinG,
                Fats = (double)p.FatG,
                Carbs = (double)p.CarbsG,
                IsDish = false
            });
        }

        AvailableReplacementItems = new ObservableCollection<FoodItemDisplayDto>(items);
        SelectedReplacementItem = AvailableReplacementItems.FirstOrDefault(i => i.Id == (slot.ProductId ?? slot.DishId))
                                  ?? AvailableReplacementItems.FirstOrDefault();

        IsReplaceDialogOpen = true;
    }

    [RelayCommand]
    private void CloseReplaceDialog() => IsReplaceDialogOpen = false;

    [RelayCommand]
    public async Task SaveReplaceSlotAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedSlotToReplace == null || SelectedReplacementItem == null) return;

        if (!decimal.TryParse(ReplacePortionGramsInput, out var grams) || grams <= 0)
        {
            StatusMessage = "Вкажіть масу у грамах більше 0.";
            return;
        }

        Guid? prodId = SelectedReplacementItem.IsDish ? null : SelectedReplacementItem.Id;
        Guid? dishId = SelectedReplacementItem.IsDish ? SelectedReplacementItem.Id : null;

        await _planService.ReplacePlanItemAsync(SelectedSlotToReplace.PlanItemId, prodId, dishId, grams, cancellationToken);

        IsReplaceDialogOpen = false;

        var currentPlanId = SelectedHistoryPlan?.Id;
        await LoadHistoryAsync(cancellationToken);
        if (currentPlanId.HasValue)
        {
            SelectedHistoryPlan = PlanHistory.FirstOrDefault(p => p.Id == currentPlanId.Value);
        }
    }
}