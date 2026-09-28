using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using DietPlanner.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public class MealIntakeService : IMealIntakeService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly INutritionCalculator _calculator;
    private readonly IRestrictionService _restrictionService;
    private readonly ILoggingService _logging;
    private readonly ILocalizationService _loc;

    public MealIntakeService(
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

    public async Task AddIntakeItemAsync(Guid userId, Guid? productId, Guid? dishId, decimal amountGrams, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        string itemName = _loc.GetString("Meal_DefaultName");
        NutritionSnapshot nutrition = new(0m, 0m, 0m, 0m);

        if (productId.HasValue)
        {
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId.Value, cancellationToken);
            if (product != null)
            {
                itemName = product.Name;
                nutrition = product.CalculateNutritionSnapshot(amountGrams);
            }
        }
        else if (dishId.HasValue)
        {
            var dish = await db.Dishes.AsNoTracking()
                .Include(d => d.Ingredients)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(d => d.Id == dishId.Value, cancellationToken);
            if (dish != null)
            {
                itemName = dish.Name;
                var baseNutr = dish.CalculateNutrition();
                nutrition = new NutritionSnapshot(
                    baseNutr.Calories * (amountGrams / 100m),
                    baseNutr.ProteinG * (amountGrams / 100m),
                    baseNutr.FatG * (amountGrams / 100m),
                    baseNutr.CarbsG * (amountGrams / 100m));
            }
        }

        var startOfDay = DateTime.UtcNow.Date;
        var endOfDay = startOfDay.AddDays(1);

        var existingIntakeId = await db.MealIntakes
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.ConsumedAtUtc >= startOfDay && m.ConsumedAtUtc < endOfDay)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        Guid intakeId;
        if (!existingIntakeId.HasValue)
        {
            var newIntake = new MealIntake(userId, DateTime.UtcNow, MealType.Snack, _loc.GetString("Meal_DefaultName"));
            db.MealIntakes.Add(newIntake);
            await db.SaveChangesAsync(cancellationToken);
            intakeId = newIntake.Id;
        }
        else
        {
            intakeId = existingIntakeId.Value;
        }

        var item = new MealIntakeItem(MealEntrySource.Manual, productId, dishId, itemName, amountGrams, _loc.GetString("Unit_Grams"), nutrition);
        db.Entry(item).Property("MealIntakeId").CurrentValue = intakeId;
        
        db.MealIntakeItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            userId,
            ActionType.Create,
            $"Added intake item: {itemName} ({amountGrams:F0}g, {nutrition.Calories:F0} kcal).",
            entityName: nameof(MealIntakeItem),
            entityId: item.Id,
            cancellationToken: cancellationToken);
    }

    public async Task RemoveIntakeItemByFoodAsync(Guid userId, Guid? productId, Guid? dishId, string itemName, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var startOfDay = DateTime.UtcNow.Date;
        var endOfDay = startOfDay.AddDays(1);

        var intake = await db.MealIntakes
            .Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.ConsumedAtUtc >= startOfDay && m.ConsumedAtUtc < endOfDay, cancellationToken);

        if (intake != null)
        {
            var itemToRemove = intake.Items.FirstOrDefault(i =>
                (productId.HasValue && i.ProductId == productId.Value) ||
                (dishId.HasValue && i.DishId == dishId.Value) ||
                i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase));

            if (itemToRemove != null)
            {
                db.MealIntakeItems.Remove(itemToRemove);
                await db.SaveChangesAsync(cancellationToken);

                await _logging.LogActionAsync(
                    userId,
                    ActionType.Delete,
                    $"Removed intake item: {itemToRemove.ItemName} ({itemToRemove.Amount:F0}g).",
                    entityName: nameof(MealIntakeItem),
                    entityId: itemToRemove.Id,
                    cancellationToken: cancellationToken);
            }
        }
    }

    public async Task<List<FoodItemDisplayDto>> SuggestMealOptionsAsync(
        Guid userId, 
        int limit = 5, 
        IEnumerable<Guid>? excludeIds = null, 
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null) return new List<FoodItemDisplayDto>();

        NutritionCalculation targetCalc;
        try
        {
            targetCalc = _calculator.Calculate(user);
        }
        catch
        {
            return new List<FoodItemDisplayDto>();
        }

        var todayIntakes = await GetTodayIntakesAsync(userId, cancellationToken);
        var currentCalories = todayIntakes.Sum(x => x.Calories);

        var remainingCalories = (double)targetCalc.Targets.Calories - currentCalories;
        if (remainingCalories < 100) return new List<FoodItemDisplayDto>();

        var excludedSet = excludeIds != null 
            ? new HashSet<Guid>(excludeIds) 
            : new HashSet<Guid>();

        var candidates = new List<(FoodItemDisplayDto Dto, double Score)>();

        var products = await db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var p in products)
        {
            if (excludedSet.Contains(p.Id)) continue;

            var check = await _restrictionService.CheckProductAsync(userId, p, cancellationToken);
            if (!check.Allowed || p.Calories <= 0) continue;

            double targetPortionCalories = Math.Min(remainingCalories, 350.0);
            double portionGrams = Math.Round((targetPortionCalories / (double)p.Calories) * 100.0, 0);
            portionGrams = Math.Clamp(portionGrams, 50.0, 400.0);

            double itemCalories = ((double)p.Calories / 100.0) * portionGrams;
            double itemProteins = ((double)p.ProteinG / 100.0) * portionGrams;

            if (itemCalories > remainingCalories + 50) continue;

            double score = Math.Abs(targetPortionCalories - itemCalories) - (itemProteins * 2.0);

            candidates.Add((new FoodItemDisplayDto
            {
                Id = p.Id,
                Name = $"{p.Name} (~{portionGrams:F0}{_loc.GetString("Unit_Grams")})",
                CategoryName = p.Category?.Name ?? _loc.GetString("Meal_ProductCategoryDefault"),
                Calories = itemCalories,
                Proteins = itemProteins,
                Fats = ((double)p.FatG / 100.0) * portionGrams,
                Carbs = ((double)p.CarbsG / 100.0) * portionGrams,
                IsDish = false
            }, score));
        }

        var dishes = await db.Dishes.AsNoTracking()
            .Include(d => d.Category)
            .Include(d => d.Ingredients)
                .ThenInclude(i => i.Product)
            .Where(d => d.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var d in dishes)
        {
            if (excludedSet.Contains(d.Id)) continue;

            var check = await _restrictionService.CheckDishAsync(userId, d, cancellationToken);
            if (!check.Allowed || d.Ingredients.Count == 0) continue;

            var baseNutr = d.CalculateNutrition();
            if (baseNutr.Calories <= 0m) continue;

            double targetPortionCalories = Math.Min(remainingCalories, 400.0);
            double portionGrams = Math.Round(((double)targetPortionCalories / (double)baseNutr.Calories) * 100.0, 0);
            portionGrams = Math.Clamp(portionGrams, 100.0, 500.0);

            double portionMultiplier = portionGrams / 100.0;
            double itemCalories = (double)baseNutr.Calories * portionMultiplier;
            double itemProteins = (double)baseNutr.ProteinG * portionMultiplier;

            if (itemCalories > remainingCalories + 50) continue;

            double score = Math.Abs(targetPortionCalories - itemCalories) - (itemProteins * 2.5);

            candidates.Add((new FoodItemDisplayDto
            {
                Id = d.Id,
                Name = $"[{_loc.GetString("Meal_DishCategoryDefault")}] {d.Name} (~{portionGrams:F0}{_loc.GetString("Unit_Grams")})",
                CategoryName = d.Category?.Name ?? _loc.GetString("Meal_DishCategoryDefault"),
                Calories = itemCalories,
                Proteins = itemProteins,
                Fats = (double)baseNutr.FatG * portionMultiplier,
                Carbs = (double)baseNutr.CarbsG * portionMultiplier,
                IsDish = true
            }, score));
        }

        return candidates
            .OrderBy(c => c.Score)
            .Select(c => c.Dto)
            .Take(limit)
            .ToList();
    }
    
    public async Task SyncPastDaysEatenItemsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        var today = DateTime.UtcNow.Date;

        var pastPlans = await db.NutritionPlans
            .Include(p => p.Items)
                .ThenInclude(i => i.Product)
            .Include(p => p.Items)
                .ThenInclude(i => i.Dish)
            .Where(p => p.UserId == userId && p.PlanDate.Date < today)
            .ToListAsync(cancellationToken);

        if (pastPlans.Count == 0) return;

        var pastIntakes = await db.MealIntakes
            .Include(i => i.Items)
            .Where(i => i.UserId == userId && i.ConsumedAtUtc.Date < today)
            .ToListAsync(cancellationToken);

        int newItemsCount = 0;

        foreach (var plan in pastPlans)
        {
            var planDate = plan.PlanDate.Date;

            var dayIntake = pastIntakes.FirstOrDefault(i => i.ConsumedAtUtc.Date == planDate);
            if (dayIntake == null)
            {
                dayIntake = new MealIntake(
                    userId, 
                    DateTime.SpecifyKind(planDate.AddHours(12), DateTimeKind.Utc), 
                    MealType.Snack, 
                    _loc.GetString("Meal_AutoFixPlan"));
                db.MealIntakes.Add(dayIntake);
                await db.SaveChangesAsync(cancellationToken);
                pastIntakes.Add(dayIntake);
            }

            foreach (var item in plan.Items)
            {
                if (!item.ProductId.HasValue && !item.DishId.HasValue) continue;

                var itemName = item.Product?.Name ?? item.Dish?.Name ?? item.MealName;

                bool isAlreadyInDb = dayIntake.Items.Any(i => 
                    (item.ProductId.HasValue && i.ProductId == item.ProductId.Value) ||
                    (item.DishId.HasValue && i.DishId == item.DishId.Value) ||
                    i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase));

                if (!isAlreadyInDb)
                {
                    var snapshot = new NutritionSnapshot(item.Calories, item.ProteinG, item.FatG, item.CarbsG);

                    var newItem = new MealIntakeItem(
                        MealEntrySource.Manual,
                        item.ProductId,
                        item.DishId,
                        itemName,
                        item.PortionAmount,
                        _loc.GetString("Unit_Grams"),
                        snapshot);

                    db.Entry(newItem).Property("MealIntakeId").CurrentValue = dayIntake.Id;
                    db.MealIntakeItems.Add(newItem);
                    dayIntake.Items.Add(newItem);
                    newItemsCount++;
                }
            }
        }

        if (newItemsCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);

            await _logging.LogActionAsync(
                userId,
                ActionType.Create,
                $"Auto-synced {newItemsCount} items from past plans.",
                entityName: nameof(MealIntakeItem),
                cancellationToken: cancellationToken);
        }
    }

    public async Task<List<MealIntakeDisplayDto>> GetTodayIntakesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var startOfDay = DateTime.UtcNow.Date;
        var endOfDay = startOfDay.AddDays(1);

        var intakes = await db.MealIntakes
            .AsNoTracking()
            .Include(i => i.Items)
            .Where(i => i.UserId == userId && i.ConsumedAtUtc >= startOfDay && i.ConsumedAtUtc < endOfDay)
            .ToListAsync(cancellationToken);

        var dtos = new List<MealIntakeDisplayDto>();
        foreach (var intake in intakes)
        {
            foreach (var item in intake.Items)
            {
                dtos.Add(new MealIntakeDisplayDto
                {
                    Id = item.Id,
                    IntakeTime = intake.ConsumedAtUtc.ToLocalTime(),
                    ItemName = item.ItemName,
                    WeightGrams = (double)item.Amount,
                    Calories = (double)item.Calories,
                    Proteins = (double)item.ProteinG,
                    Fats = (double)item.FatG,
                    Carbs = (double)item.CarbsG,
                    NutritionSummary = string.Format(_loc.GetString("Meal_MacrosSummary"), item.ProteinG, item.FatG, item.CarbsG)
                });
            }
        }

        return dtos;
    }

    public async Task DeleteIntakeItemByIdAsync(Guid itemId, Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.MealIntakeItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item != null)
        {
            db.MealIntakeItems.Remove(item);
            await db.SaveChangesAsync(cancellationToken);

            await _logging.LogActionAsync(
                userId,
                ActionType.Delete,
                $"Deleted intake item: {item.ItemName} ({item.Amount:F0}g).",
                entityName: nameof(MealIntakeItem),
                entityId: item.Id,
                cancellationToken: cancellationToken);
        }
    }

    public async Task UpdateIntakeItemAmountAsync(Guid itemId, decimal newAmountGrams, Guid userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.MealIntakeItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item == null) return;

        NutritionSnapshot nutrition = new(0m, 0m, 0m, 0m);

        if (item.ProductId.HasValue)
        {
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.ProductId.Value, cancellationToken);
            if (product != null)
            {
                nutrition = product.CalculateNutritionSnapshot(newAmountGrams);
            }
        }
        else if (item.DishId.HasValue)
        {
            var dish = await db.Dishes.AsNoTracking().Include(d => d.Ingredients).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(d => d.Id == item.DishId.Value, cancellationToken);
            if (dish != null)
            {
                var baseNutr = dish.CalculateNutrition();
                nutrition = new NutritionSnapshot(
                    baseNutr.Calories * (newAmountGrams / 100m),
                    baseNutr.ProteinG * (newAmountGrams / 100m),
                    baseNutr.FatG * (newAmountGrams / 100m),
                    baseNutr.CarbsG * (newAmountGrams / 100m));
            }
        }

        db.Entry(item).Property("Amount").CurrentValue = newAmountGrams;
        db.Entry(item).Property("Calories").CurrentValue = nutrition.Calories;
        db.Entry(item).Property("ProteinG").CurrentValue = nutrition.ProteinG;
        db.Entry(item).Property("FatG").CurrentValue = nutrition.FatG;
        db.Entry(item).Property("CarbsG").CurrentValue = nutrition.CarbsG;

        await db.SaveChangesAsync(cancellationToken);

        await _logging.LogActionAsync(
            userId,
            ActionType.Update,
            $"Updated intake portion: {item.ItemName} to {newAmountGrams:F0}g.",
            entityName: nameof(MealIntakeItem),
            entityId: item.Id,
            cancellationToken: cancellationToken);
    }
}