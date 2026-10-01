using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public class MealSlotDisplayDto : ObservableObject
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

    public int ComponentCount { get; set; } = 1;
    public bool IsMultiComponent => ComponentCount > 1;
    public string MultiComponentNote { get; set; } = string.Empty;
}

public partial class PlanGeneratorViewModel : ViewModelBase
{
    private readonly IPlanService _planService;
    private readonly IAuthenticationService _authService;
    private readonly INutritionCalculator _calculator;
    private readonly IProductService _productService;
    private readonly IDishService _dishService;
    private readonly ILocalizationService _loc;

    [ObservableProperty] private int _mealCount = 4;
    
    [ObservableProperty] private decimal _calculatedTargetCalories = 2000m;
    [ObservableProperty] private string _calorieRangeText = "1900 – 2100 ккал/день";

    [ObservableProperty] private ObservableCollection<MealSlotDisplayDto> _generatedSlots = new();
    [ObservableProperty] private ObservableCollection<NutritionPlan> _planHistory = new();
    [ObservableProperty] private NutritionPlan? _selectedHistoryPlan;

    [ObservableProperty] private string _generationResultSummary = string.Empty;
    [ObservableProperty] private string _userSummaryInfo = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

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
        IDishService dishService,
        ILocalizationService loc)
    {
        _planService = planService;
        _authService = authService;
        _calculator = calculator;
        _productService = productService;
        _dishService = dishService;
        _loc = loc;

        LoadUserProfileAndTargets();
    }

    partial void OnMealCountChanged(int value)
    {
        var normalized = Math.Clamp(value, 1, 8);
        if (value != normalized)
        {
            MealCount = normalized;
        }
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

        if (value.Items != null && value.Items.Count > 0)
        {
            var multiNoteFormat = _loc.GetString("Plan_MultiComponentNoteFormat");
            var dishPrefixFormat = _loc.GetString("Plan_DishPrefixFormat");

            var grouped = value.Items
                .GroupBy(x => x.MealName)
                .Select(group =>
                {
                    var first = group.First();
                    var names = group.Select(item =>
                        item.Dish != null ? string.Format(dishPrefixFormat, item.Dish.Name) : (item.Product?.Name ?? item.MealName));

                    var count = group.Count();

                    return new MealSlotDisplayDto
                    {
                        PlanItemId = first.Id,
                        SlotName = group.Key,
                        SuggestedItemName = string.Join(" + ", names),
                        PortionGrams = (double)group.Sum(x => x.PortionAmount),
                        Calories = (double)group.Sum(x => x.Calories),
                        Proteins = (double)group.Sum(x => x.ProteinG),
                        Fats = (double)group.Sum(x => x.FatG),
                        Carbs = (double)group.Sum(x => x.CarbsG),
                        ProductId = first.ProductId,
                        DishId = first.DishId,
                        ComponentCount = count,
                        MultiComponentNote = count > 1 ? string.Format(multiNoteFormat, count) : string.Empty
                    };
                });

            foreach (var slot in grouped)
            {
                GeneratedSlots.Add(slot);
            }
        }

        GenerationResultSummary = string.Format(_loc.GetString("Plan_SummaryLoadedFormat"), value.PlanDate, value.TargetCalories);
    }

    [RelayCommand]
    public async Task GenerateDayPlanAsync(CancellationToken cancellationToken = default)
    {
        var user = _authService.CurrentUser;
        if (user == null) return;

        StatusMessage = _loc.GetString("Plan_StatusGeneratingDay");
        
        var result = await _planService.GenerateAsync(user.Id, MealCount, CalculatedTargetCalories, cancellationToken);

        StatusMessage = result.Message;
        GenerationResultSummary = result.Message;

        if (!result.Success || result.Plan == null) return;

        await LoadHistoryAsync(cancellationToken);
        SelectedHistoryPlan = PlanHistory.FirstOrDefault(p => p.Id == result.Plan.Id);
    }

    [RelayCommand]
    public async Task GenerateWeekPlanAsync(CancellationToken cancellationToken = default)
    {
        var user = _authService.CurrentUser;
        if (user == null) return;

        StatusMessage = _loc.GetString("Plan_StatusGeneratingWeek");

        var results = await _planService.GenerateWeekAsync(user.Id, MealCount, CalculatedTargetCalories, cancellationToken);

        await LoadHistoryAsync(cancellationToken);

        if (results.Count > 0 && results[0].Plan != null)
        {
            SelectedHistoryPlan = PlanHistory.FirstOrDefault(p => p.Id == results[0].Plan!.Id);
            StatusMessage = _loc.GetString("Plan_StatusWeekSuccess");
            GenerationResultSummary = _loc.GetString("Plan_SummaryWeekSuccess");
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
                Name = $"[{_loc.GetString("Meal_DishCategoryDefault")}] {d.Name} ({nutr.Calories:F0} {_loc.GetString("Unit_Kcal")}/100{_loc.GetString("Unit_Grams")})",
                CategoryName = d.Category?.Name ?? _loc.GetString("Meal_DishCategoryDefault"),
                Calories = (double)nutr.Calories,
                Proteins = (double)nutr.ProteinG,
                Fats = (double)nutr.FatG,
                Carbs = (double)nutr.CarbsG,
                IsDish = true
            });
        }

        var products = await _productService.GetGlobalAsync(cancellationToken: cancellationToken);
        foreach (var p in products)
        {
            items.Add(new FoodItemDisplayDto
            {
                Id = p.Id,
                Name = $"{p.Name} ({p.Calories:F0} {_loc.GetString("Unit_Kcal")}/100{_loc.GetString("Unit_Grams")})",
                CategoryName = p.Category?.Name ?? _loc.GetString("Meal_ProductCategoryDefault"),
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
            StatusMessage = _loc.GetString("Plan_ValidGramsRequired");
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