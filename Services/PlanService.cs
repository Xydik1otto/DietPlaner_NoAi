using Microsoft.EntityFrameworkCore;
using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public class PlanService : IPlanService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly INutritionCalculator _calculator;
    private readonly IRestrictionService _restrictionService;
    private readonly ILoggingService _logging;
    private readonly ILocalizationService _loc;

    private readonly record struct MealSlot(MealType MealType, string MealName, decimal TargetCalories);

    private enum FoodRole { FullDish, Protein, CarbGarnish, Veggie, General }

    private sealed class CandidateItem
    {
        public Product? Product { get; init; }
        public Dish? Dish { get; init; }
        public string Name { get; init; } = string.Empty;
        public decimal BaseCalories100g { get; init; }
        public decimal BaseProtein100g { get; init; }
        public decimal BaseFat100g { get; init; }
        public decimal BaseCarbs100g { get; init; }
        public bool IsDish => Dish != null;
        public Guid Id => Dish?.Id ?? Product?.Id ?? Guid.Empty;
        public FoodRole Role { get; init; }
    }

    public PlanService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        INutritionCalculator calculator,
        IRestrictionService restrictionService,
        ILoggingService logging,
        ILocalizationService loc)
    {
        _dbContextFactory = dbContextFactory;
        _calculator = calculator;
        _restrictionService = restrictionService;
        _logging = logging;
        _loc = loc;
    }

    public async Task<PlanGenerationResult> GenerateAsync(Guid userId, int mealCount, decimal? targetCalories = null, CancellationToken cancellationToken = default)
    {
        var globalUsedCandidateIds = await GetRecentUsedCandidateIdsAsync(userId, cancellationToken);
        return await GenerateSingleDayPlanAsync(userId, mealCount, targetCalories, DateTime.UtcNow.Date, globalUsedCandidateIds, cancellationToken);
    }

    public async Task<IReadOnlyList<PlanGenerationResult>> GenerateWeekAsync(Guid userId, int mealCount, decimal? targetCalories = null, CancellationToken cancellationToken = default)
    {
        var results = new List<PlanGenerationResult>();
        var globalUsedCandidateIds = await GetRecentUsedCandidateIdsAsync(userId, cancellationToken);
        var startDate = DateTime.UtcNow.Date;

        for (int dayOffset = 0; dayOffset < 7; dayOffset++)
        {
            var planDate = startDate.AddDays(dayOffset);
            var result = await GenerateSingleDayPlanAsync(userId, mealCount, targetCalories, planDate, globalUsedCandidateIds, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    private async Task<HashSet<Guid>> GetRecentUsedCandidateIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cutoffDate = DateTime.UtcNow.Date.AddDays(-5);
        
        var recentItems = await db.PlanItems
            .Where(pi => pi.NutritionPlan!.UserId == userId && pi.NutritionPlan.PlanDate >= cutoffDate)
            .Select(pi => new { pi.ProductId, pi.DishId })
            .ToListAsync(cancellationToken);

        var usedIds = new HashSet<Guid>();
        foreach (var item in recentItems)
        {
            if (item.ProductId.HasValue) usedIds.Add(item.ProductId.Value);
            if (item.DishId.HasValue) usedIds.Add(item.DishId.Value);
        }

        return usedIds;
    }

    private async Task<PlanGenerationResult> GenerateSingleDayPlanAsync(
        Guid userId, 
        int mealCount, 
        decimal? targetCalories, 
        DateTime planDate, 
        HashSet<Guid> globalUsedCandidateIds,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
        {
            return new PlanGenerationResult(false, _loc.GetString("Err_UserNotFound"), null, null, Array.Empty<string>());
        }

        NutritionCalculation calculation;
        try
        {
            calculation = _calculator.Calculate(user);
        }
        catch (Exception ex)
        {
            return new PlanGenerationResult(false, ex.Message, null, null, Array.Empty<string>());
        }

        decimal baseTarget = targetCalories ?? calculation.Targets.Calories;
        double variancePercent = (Random.Shared.NextDouble() * 0.08) - 0.04;
        decimal effectiveCalories = Math.Round(baseTarget * (decimal)(1.0 + variancePercent));

        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync(cancellationToken);
        var allowedProducts = new List<Product>();
        foreach (var p in products)
        {
            var check = await _restrictionService.CheckProductAsync(userId, p, cancellationToken);
            if (check.Allowed && p.Calories > 0m) allowedProducts.Add(p);
        }

        var dishes = await db.Dishes.AsNoTracking()
            .Include(d => d.Ingredients).ThenInclude(i => i.Product)
            .Where(d => d.IsActive).ToListAsync(cancellationToken);

        var allowedDishes = new List<Dish>();
        foreach (var d in dishes)
        {
            var check = await _restrictionService.CheckDishAsync(userId, d, cancellationToken);
            if (check.Allowed && d.Ingredients.Count > 0) allowedDishes.Add(d);
        }

        if (allowedProducts.Count == 0 && allowedDishes.Count == 0)
        {
            return new PlanGenerationResult(
                false,
                _loc.GetString("Plan_NoItemsError"),
                null, null, new[] { _loc.GetString("Plan_AddDishesHint") });
        }

        var candidates = new List<CandidateItem>();

        foreach (var d in allowedDishes)
        {
            var baseNutr = d.CalculateNutrition();
            if (baseNutr.Calories <= 0m) continue;

            candidates.Add(new CandidateItem
            {
                Dish = d,
                Name = d.Name,
                BaseCalories100g = baseNutr.Calories,
                BaseProtein100g = baseNutr.ProteinG,
                BaseFat100g = baseNutr.FatG,
                BaseCarbs100g = baseNutr.CarbsG,
                Role = FoodRole.FullDish
            });
        }

        foreach (var p in allowedProducts)
        {
            candidates.Add(new CandidateItem
            {
                Product = p,
                Name = p.Name,
                BaseCalories100g = p.Calories,
                BaseProtein100g = p.ProteinG,
                BaseFat100g = p.FatG,
                BaseCarbs100g = p.CarbsG,
                Role = ClassifyProductRole(p)
            });
        }

        var slotQueue = BuildMealQueue(mealCount, effectiveCalories);

        var targetProtein = effectiveCalories * 0.30m / 4m;
        var targetFat = effectiveCalories * 0.25m / 9m;
        var targetCarbs = effectiveCalories * 0.45m / 4m;
        var targets = new NutritionTargets(effectiveCalories, targetProtein, targetFat, targetCarbs);

        var plan = new NutritionPlan(userId, planDate, mealCount, targets);
        var dailyUsedIds = new HashSet<Guid>();

        while (slotQueue.Count > 0)
        {
            var slot = slotQueue.Dequeue();

            if (slot.MealType != MealType.Snack && candidates.Any(c => c.Role == FoodRole.Protein) && candidates.Any(c => c.Role == FoodRole.CarbGarnish))
            {
                var fullDishes = candidates.Where(c => c.IsDish).ToList();
                if (fullDishes.Count > 0 && Random.Shared.NextDouble() < 0.4)
                {
                    AddSingleCandidateToPlan(plan, slot, SelectBestCandidate(fullDishes, slot, dailyUsedIds, globalUsedCandidateIds), dailyUsedIds, globalUsedCandidateIds);
                }
                else
                {
                    AssembleComboPlate(plan, slot, candidates, dailyUsedIds, globalUsedCandidateIds);
                }
            }
            else
            {
                AddSingleCandidateToPlan(plan, slot, SelectBestCandidate(candidates, slot, dailyUsedIds, globalUsedCandidateIds), dailyUsedIds, globalUsedCandidateIds);
            }
        }

        db.NutritionPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            userId,
            ActionType.Create,
            string.Format(_loc.GetString("Plan_LogCreated"), plan.PlanDate, mealCount, effectiveCalories),
            entityName: nameof(NutritionPlan),
            entityId: plan.Id,
            cancellationToken: cancellationToken);

        var loadedPlan = await GetForDateAsync(userId, plan.PlanDate, cancellationToken);
        var deviation = loadedPlan?.CalculateDeviation();

        return new PlanGenerationResult(
            true,
            string.Format(_loc.GetString("Plan_GenSuccess"), planDate, effectiveCalories),
            loadedPlan,
            deviation,
            Array.Empty<string>());
    }

    private static FoodRole ClassifyProductRole(Product p)
    {
        var nameLower = p.Name.ToLowerInvariant();

        if (nameLower.Contains("броколі") || nameLower.Contains("огірок") || nameLower.Contains("томат") || 
            nameLower.Contains("помідор") || nameLower.Contains("салат") || nameLower.Contains("капуста") || 
            nameLower.Contains("яблуко") || nameLower.Contains("перець") || nameLower.Contains("broccoli") ||
            nameLower.Contains("cucumber") || nameLower.Contains("tomato") || nameLower.Contains("apple"))
        {
            return FoodRole.Veggie;
        }

        double cal = (double)p.Calories;
        if (cal <= 0) return FoodRole.General;

        double proteinCalRatio = (double)(p.ProteinG * 4m) / cal;
        double carbsCalRatio = (double)(p.CarbsG * 4m) / cal;

        if (proteinCalRatio >= 0.35) return FoodRole.Protein;
        if (carbsCalRatio >= 0.45 && cal >= 90) return FoodRole.CarbGarnish;
        if (cal < 80) return FoodRole.Veggie;

        return FoodRole.General;
    }

    private void AssembleComboPlate(
        NutritionPlan plan, 
        MealSlot slot, 
        List<CandidateItem> candidates, 
        HashSet<Guid> dailyUsedIds, 
        HashSet<Guid> globalUsedIds)
    {
        decimal proteinTargetCal = slot.TargetCalories * 0.40m;
        decimal carbTargetCal = slot.TargetCalories * 0.45m;
        decimal veggieTargetCal = slot.TargetCalories * 0.15m;

        var proteins = candidates.Where(c => c.Role == FoodRole.Protein || c.BaseProtein100g >= 12m).ToList();
        var carbs = candidates.Where(c => c.Role == FoodRole.CarbGarnish || c.BaseCarbs100g >= 18m).ToList();
        var veggies = candidates.Where(c => c.Role == FoodRole.Veggie || c.BaseCalories100g < 100m).ToList();

        if (proteins.Count > 0)
        {
            var bestProtein = SelectBestCandidate(proteins, slot, dailyUsedIds, globalUsedIds);
            decimal portion = Math.Clamp(Math.Round((proteinTargetCal / bestProtein.BaseCalories100g) * 100m), 100m, 200m);
            AddPlanItem(plan, slot, bestProtein, portion, dailyUsedIds, globalUsedIds);
        }

        if (carbs.Count > 0)
        {
            var bestCarb = SelectBestCandidate(carbs, slot, dailyUsedIds, globalUsedIds);
            decimal portion = Math.Clamp(Math.Round((carbTargetCal / bestCarb.BaseCalories100g) * 100m), 100m, 200m);
            AddPlanItem(plan, slot, bestCarb, portion, dailyUsedIds, globalUsedIds);
        }

        if (veggies.Count > 0)
        {
            var bestVeggie = SelectBestCandidate(veggies, slot, dailyUsedIds, globalUsedIds);
            decimal portion = Math.Clamp(Math.Round((veggieTargetCal / bestVeggie.BaseCalories100g) * 100m), 80m, 150m);
            AddPlanItem(plan, slot, bestVeggie, portion, dailyUsedIds, globalUsedIds);
        }
    }

    private void AddSingleCandidateToPlan(
        NutritionPlan plan, 
        MealSlot slot, 
        CandidateItem candidate, 
        HashSet<Guid> dailyUsedIds, 
        HashSet<Guid> globalUsedIds)
    {
        decimal maxPortion = candidate.IsDish ? 450m : (candidate.Role == FoodRole.Veggie ? 150m : 220m);
        decimal minPortion = candidate.IsDish ? 150m : 60m;

        decimal portionGrams = Math.Clamp(Math.Round((slot.TargetCalories / candidate.BaseCalories100g) * 100m), minPortion, maxPortion);
        AddPlanItem(plan, slot, candidate, portionGrams, dailyUsedIds, globalUsedIds);
    }

    private void AddPlanItem(
        NutritionPlan plan, 
        MealSlot slot, 
        CandidateItem candidate, 
        decimal portionGrams, 
        HashSet<Guid> dailyUsedIds, 
        HashSet<Guid> globalUsedIds)
    {
        dailyUsedIds.Add(candidate.Id);
        globalUsedIds.Add(candidate.Id);

        var multiplier = portionGrams / 100m;
        var nutrition = new NutritionSnapshot(
            candidate.BaseCalories100g * multiplier,
            candidate.BaseProtein100g * multiplier,
            candidate.BaseFat100g * multiplier,
            candidate.BaseCarbs100g * multiplier);

        plan.Items.Add(new PlanItem(
            slot.MealType,
            slot.MealName,
            candidate.Product?.Id,
            candidate.Dish?.Id,
            portionGrams,
            _loc.GetString("Unit_Grams"),
            nutrition));
    }

    private static CandidateItem SelectBestCandidate(
        List<CandidateItem> list, 
        MealSlot slot, 
        HashSet<Guid> dailyUsedIds, 
        HashSet<Guid> globalUsedIds)
    {
        var scored = new List<(CandidateItem Item, double Score)>();

        foreach (var c in list)
        {
            double score = 100.0;

            if (dailyUsedIds.Contains(c.Id)) score -= 300.0;
            else if (globalUsedIds.Contains(c.Id)) score -= 120.0;

            score += Random.Shared.NextDouble() * 50.0;
            scored.Add((c, score));
        }

        var top = scored.OrderByDescending(x => x.Score).Take(3).ToList();
        return top.Count > 0 ? top[Random.Shared.Next(top.Count)].Item : list[Random.Shared.Next(list.Count)];
    }

    public async Task<NutritionPlan?> GetForDateAsync(Guid userId, DateTime date, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.NutritionPlans
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .Include(p => p.Items).ThenInclude(i => i.Dish)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PlanDate.Date == date.Date, cancellationToken);
    }

    public async Task<IReadOnlyList<NutritionPlan>> GetHistoryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.NutritionPlans
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .Include(p => p.Items).ThenInclude(i => i.Dish)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ReplacePlanItemAsync(Guid planItemId, Guid? newProductId, Guid? newDishId, decimal portionGrams, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.PlanItems.FindAsync(new object[] { planItemId }, cancellationToken);
        if (item == null) return false;

        NutritionSnapshot nutrition = new(0m, 0m, 0m, 0m);

        if (newProductId.HasValue)
        {
            var product = await db.Products.FindAsync(new object[] { newProductId.Value }, cancellationToken);
            if (product != null)
            {
                nutrition = _calculator.CalculateProductPortion(product, portionGrams);
            }
        }
        else if (newDishId.HasValue)
        {
            var dish = await db.Dishes.Include(d => d.Ingredients).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(d => d.Id == newDishId.Value, cancellationToken);
            if (dish != null)
            {
                var baseNutr = dish.CalculateNutrition();
                nutrition = new NutritionSnapshot(
                    baseNutr.Calories * (portionGrams / 100m),
                    baseNutr.ProteinG * (portionGrams / 100m),
                    baseNutr.FatG * (portionGrams / 100m),
                    baseNutr.CarbsG * (portionGrams / 100m));
            }
        }

        item.UpdateFoodItem(newProductId, newDishId, portionGrams, nutrition);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Queue<MealSlot> BuildMealQueue(int mealCount, decimal totalCalories)
    {
        var queue = new Queue<MealSlot>();
        mealCount = Math.Clamp(mealCount, 3, 6);

        var breakfast = _loc.GetString("Meal_Breakfast");
        var lunch = _loc.GetString("Meal_Lunch");
        var dinner = _loc.GetString("Meal_Dinner");
        var snack = _loc.GetString("Meal_Snack");
        var secondBreakfast = _loc.GetString("Meal_SecondBreakfast");
        var lateDinner = _loc.GetString("Meal_LateDinner");

        switch (mealCount)
        {
            case 3:
                queue.Enqueue(new MealSlot(MealType.Breakfast, breakfast, totalCalories * 0.30m));
                queue.Enqueue(new MealSlot(MealType.Lunch, lunch, totalCalories * 0.40m));
                queue.Enqueue(new MealSlot(MealType.Dinner, dinner, totalCalories * 0.30m));
                break;
            case 4:
                queue.Enqueue(new MealSlot(MealType.Breakfast, breakfast, totalCalories * 0.25m));
                queue.Enqueue(new MealSlot(MealType.Lunch, lunch, totalCalories * 0.35m));
                queue.Enqueue(new MealSlot(MealType.Snack, snack, totalCalories * 0.15m));
                queue.Enqueue(new MealSlot(MealType.Dinner, dinner, totalCalories * 0.25m));
                break;
            case 5:
                queue.Enqueue(new MealSlot(MealType.Breakfast, breakfast, totalCalories * 0.20m));
                queue.Enqueue(new MealSlot(MealType.Snack, secondBreakfast, totalCalories * 0.10m));
                queue.Enqueue(new MealSlot(MealType.Lunch, lunch, totalCalories * 0.35m));
                queue.Enqueue(new MealSlot(MealType.Snack, snack, totalCalories * 0.10m));
                queue.Enqueue(new MealSlot(MealType.Dinner, dinner, totalCalories * 0.25m));
                break;
            case 6:
                queue.Enqueue(new MealSlot(MealType.Breakfast, breakfast, totalCalories * 0.20m));
                queue.Enqueue(new MealSlot(MealType.Snack, secondBreakfast, totalCalories * 0.10m));
                queue.Enqueue(new MealSlot(MealType.Lunch, lunch, totalCalories * 0.30m));
                queue.Enqueue(new MealSlot(MealType.Snack, snack, totalCalories * 0.10m));
                queue.Enqueue(new MealSlot(MealType.Dinner, dinner, totalCalories * 0.20m));
                queue.Enqueue(new MealSlot(MealType.Snack, lateDinner, totalCalories * 0.10m));
                break;
        }

        return queue;
    }
}