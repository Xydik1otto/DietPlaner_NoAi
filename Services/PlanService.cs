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
    private readonly SessionService _session;

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
        ILocalizationService loc,
        SessionService session)
    {
        _dbContextFactory = dbContextFactory;
        _calculator = calculator;
        _restrictionService = restrictionService;
        _logging = logging;
        _loc = loc;
        _session = session;
    }

    public async Task<PlanGenerationResult> GenerateAsync(Guid userId, int mealCount, decimal? targetCalories = null, CancellationToken cancellationToken = default)
    {
        EnsureCanManagePlansFor(userId);
        mealCount = NormalizeMealCount(mealCount);
        var globalUsedCandidateIds = await GetRecentUsedCandidateIdsAsync(userId, cancellationToken);
        return await GenerateSingleDayPlanAsync(userId, mealCount, targetCalories, DateTime.UtcNow.Date, globalUsedCandidateIds, cancellationToken);
    }

    public async Task<IReadOnlyList<PlanGenerationResult>> GenerateWeekAsync(Guid userId, int mealCount, decimal? targetCalories = null, CancellationToken cancellationToken = default)
    {
        EnsureCanManagePlansFor(userId);
        mealCount = NormalizeMealCount(mealCount);
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

        var productsQuery = db.Products.AsNoTracking().Where(p => p.IsActive && p.IsGlobal);
        if (user.Role == UserRole.User)
        {
            productsQuery = productsQuery.Where(p => p.UserCatalogItems.Any(link => link.UserId == userId));
        }

        var products = await productsQuery.ToListAsync(cancellationToken);
        var allowedProducts = new List<Product>();
        foreach (var p in products)
        {
            var check = await _restrictionService.CheckProductAsync(userId, p, cancellationToken);
            if (check.Allowed && p.Calories > 0m) allowedProducts.Add(p);
        }

        var visibleDishesQuery = db.Dishes.AsNoTracking()
            .Include(d => d.Ingredients).ThenInclude(i => i.Product)
            .Where(d => d.IsActive);

        if (user.Role == UserRole.Admin)
            visibleDishesQuery = visibleDishesQuery.Where(d => d.OwnerUserId == null);
        else
            visibleDishesQuery = visibleDishesQuery.Where(d => d.OwnerUserId == null || d.OwnerUserId == userId);

        var dishes = await visibleDishesQuery.ToListAsync(cancellationToken);

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
        var allSlots = slotQueue.ToList();

        // Цілі БЖВ беремо з калькулятора, пропорційно масштабовані під effectiveCalories.
        var calcTargets = calculation.Targets;
        decimal targetProtein, targetFat, targetCarbs;
        if (calcTargets.Calories > 0m && calcTargets.ProteinG > 0m && calcTargets.FatG > 0m && calcTargets.CarbsG > 0m)
        {
            var scale = effectiveCalories / calcTargets.Calories;
            targetProtein = calcTargets.ProteinG * scale;
            targetFat = calcTargets.FatG * scale;
            targetCarbs = calcTargets.CarbsG * scale;
        }
        else
        {
            targetProtein = effectiveCalories * 0.30m / 4m;
            targetFat = effectiveCalories * 0.25m / 9m;
            targetCarbs = effectiveCalories * 0.45m / 4m;
        }
        var targets = new NutritionTargets(effectiveCalories, targetProtein, targetFat, targetCarbs);

        var plan = new NutritionPlan(userId, planDate, mealCount, targets);
        var dailyUsedIds = new HashSet<Guid>();
        var work = new List<WorkItem>();

        while (slotQueue.Count > 0)
        {
            var slot = slotQueue.Dequeue();

            if (slot.MealType != MealType.Snack && candidates.Any(c => c.Role == FoodRole.Protein) && candidates.Any(c => c.Role == FoodRole.CarbGarnish))
            {
                var fullDishes = candidates.Where(c => c.IsDish).ToList();
                if (fullDishes.Count > 0 && Random.Shared.NextDouble() < 0.4)
                {
                    AddSingleCandidateToPlan(work, slot, SelectBestCandidate(fullDishes, slot, dailyUsedIds, globalUsedCandidateIds), dailyUsedIds, globalUsedCandidateIds);
                }
                else
                {
                    AssembleComboPlate(work, slot, candidates, dailyUsedIds, globalUsedCandidateIds);
                }
            }
            else
            {
                AddSingleCandidateToPlan(work, slot, SelectBestCandidate(candidates, slot, dailyUsedIds, globalUsedCandidateIds), dailyUsedIds, globalUsedCandidateIds);
            }
        }

        // Підгонка порцій під добові калорії та БЖВ (+ докладання страв, якщо порції впираються в ліміти)
        FitPortionsToTargets(work, allSlots, candidates, targets, dailyUsedIds, globalUsedCandidateIds);

        foreach (var w in work)
        {
            plan.Items.Add(new PlanItem(
                w.Slot.MealType,
                w.Slot.MealName,
                w.Candidate.Product?.Id,
                w.Candidate.Dish?.Id,
                w.Grams,
                _loc.GetString("Unit_Grams"),
                new NutritionSnapshot(w.Calories, w.Protein, w.Fat, w.Carbs)));
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

    private sealed class WorkItem
    {
        public WorkItem(MealSlot slot, CandidateItem candidate, decimal grams)
        {
            Slot = slot;
            Candidate = candidate;
            Grams = grams;
        }

        public MealSlot Slot { get; }
        public CandidateItem Candidate { get; }
        public decimal Grams { get; set; }

        public decimal Calories => Candidate.BaseCalories100g * Grams / 100m;
        public decimal Protein => Candidate.BaseProtein100g * Grams / 100m;
        public decimal Fat => Candidate.BaseFat100g * Grams / 100m;
        public decimal Carbs => Candidate.BaseCarbs100g * Grams / 100m;
    }

    private void AssembleComboPlate(
        List<WorkItem> work,
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
            AddPlanItem(work, slot, bestProtein, InitialPortion(bestProtein, proteinTargetCal), dailyUsedIds, globalUsedIds);
        }

        if (carbs.Count > 0)
        {
            var bestCarb = SelectBestCandidate(carbs, slot, dailyUsedIds, globalUsedIds);
            AddPlanItem(work, slot, bestCarb, InitialPortion(bestCarb, carbTargetCal), dailyUsedIds, globalUsedIds);
        }

        if (veggies.Count > 0)
        {
            var bestVeggie = SelectBestCandidate(veggies, slot, dailyUsedIds, globalUsedIds);
            AddPlanItem(work, slot, bestVeggie, InitialPortion(bestVeggie, veggieTargetCal), dailyUsedIds, globalUsedIds);
        }
    }

    private void AddSingleCandidateToPlan(
        List<WorkItem> work,
        MealSlot slot,
        CandidateItem candidate,
        HashSet<Guid> dailyUsedIds,
        HashSet<Guid> globalUsedIds)
    {
        AddPlanItem(work, slot, candidate, InitialPortion(candidate, slot.TargetCalories), dailyUsedIds, globalUsedIds);
    }

    private void AddPlanItem(
        List<WorkItem> work,
        MealSlot slot,
        CandidateItem candidate,
        decimal portionGrams,
        HashSet<Guid> dailyUsedIds,
        HashSet<Guid> globalUsedIds)
    {
        dailyUsedIds.Add(candidate.Id);
        globalUsedIds.Add(candidate.Id);
        work.Add(new WorkItem(slot, candidate, portionGrams));
    }

    // Реалістичні межі порції (г). Було: страва 150–450, продукт 60–220, у комбо-тарілці 100–200.
    private static (decimal Min, decimal Max) GetPortionBounds(CandidateItem c)
    {
        if (c.IsDish) return (100m, 500m);

        return c.Role switch
        {
            FoodRole.Veggie => (50m, 300m),
            FoodRole.Protein => (60m, 350m),
            FoodRole.CarbGarnish => (50m, 300m),
            _ => (30m, 250m)
        };
    }

    private static decimal InitialPortion(CandidateItem c, decimal targetCalories)
    {
        var (min, max) = GetPortionBounds(c);
        if (c.BaseCalories100g <= 0m) return min;
        return Math.Clamp(Math.Round(targetCalories / c.BaseCalories100g * 100m), min, max);
    }

    private void FitPortionsToTargets(
        List<WorkItem> work,
        IReadOnlyList<MealSlot> slots,
        List<CandidateItem> candidates,
        NutritionTargets targets,
        HashSet<Guid> dailyUsedIds,
        HashSet<Guid> globalUsedIds)
    {
        if (work.Count == 0) return;

        int maxExtras = slots.Count * 2;

        for (int extra = 0; ; extra++)
        {
            OptimizePortions(work, slots, targets);
            if (extra >= maxExtras) break;

            decimal totalCal = work.Sum(w => w.Calories);
            if (targets.Calories - totalCal < targets.Calories * 0.03m) break; // недобір < 3% - ок

            // Слот з найбільшим невикористаним запасом по калоріях
            MealSlot? bestSlot = null;
            decimal bestRoom = 0m;
            foreach (var s in slots)
            {
                var used = work.Where(w => w.Slot.Equals(s)).Sum(w => w.Calories);
                var room = s.TargetCalories - used;
                if (room > bestRoom)
                {
                    bestRoom = room;
                    bestSlot = s;
                }
            }

            if (bestSlot is null || bestRoom < 80m) break;
            var slot = bestSlot.Value;

            // Який макро найбільше недобрано - під нього і підбираємо додаткову страву/продукт
            double dP = Math.Max(0.0, (double)(targets.ProteinG - work.Sum(w => w.Protein))) * 4.0;
            double dF = Math.Max(0.0, (double)(targets.FatG - work.Sum(w => w.Fat))) * 9.0;
            double dC = Math.Max(0.0, (double)(targets.CarbsG - work.Sum(w => w.Carbs))) * 4.0;
            if (dP + dF + dC <= 0.0) { dP = 0.30; dF = 0.25; dC = 0.45; }
            double dNorm = Math.Sqrt(dP * dP + dF * dF + dC * dC);

            var pool = candidates
                .Where(c => c.BaseCalories100g >= 60m &&
                            !work.Any(w => w.Slot.Equals(slot) && w.Candidate.Id == c.Id))
                .ToList();
            if (pool.Count == 0) break;

            var scored = pool.Select(c =>
            {
                double p = (double)(c.BaseProtein100g * 4m);
                double f = (double)(c.BaseFat100g * 9m);
                double cb = (double)(c.BaseCarbs100g * 4m);
                double norm = Math.Sqrt(p * p + f * f + cb * cb);
                double cos = norm > 0 ? (p * dP + f * dF + cb * dC) / (norm * dNorm) : 0.0;
                if (dailyUsedIds.Contains(c.Id)) cos -= 0.35;
                cos += Random.Shared.NextDouble() * 0.15;
                return (Item: c, Score: cos);
            })
            .OrderByDescending(x => x.Score)
            .Take(3)
            .ToList();

            var pick = scored[Random.Shared.Next(scored.Count)].Item;
            AddPlanItem(work, slot, pick, InitialPortion(pick, bestRoom), dailyUsedIds, globalUsedIds);
        }
    }

    /// <summary>
    /// Підбирає грами кожної позиції так, щоб мінімізувати відхилення від добових калорій, Б, Ж, В
    /// та калорій кожного прийому (покоординатний спуск з обмеженнями на порцію).
    /// </summary>
    private static void OptimizePortions(List<WorkItem> work, IReadOnlyList<MealSlot> slots, NutritionTargets t)
    {
        int n = work.Count;
        var u = new double[n];   // порція в "сотнях грамів"
        var lo = new double[n];
        var hi = new double[n];

        for (int i = 0; i < n; i++)
        {
            var (min, max) = GetPortionBounds(work[i].Candidate);
            lo[i] = (double)min / 100.0;
            hi[i] = (double)max / 100.0;
            u[i] = Math.Clamp((double)work[i].Grams / 100.0, lo[i], hi[i]);
        }

        var coefs = new List<double[]>();
        var rowTargets = new List<double>();
        var weights = new List<double>();

        void AddRow(Func<WorkItem, double> coef, decimal target, double weight)
        {
            if (target <= 0m) return;
            coefs.Add(work.Select(coef).ToArray());
            rowTargets.Add((double)target);
            weights.Add(weight / ((double)target * (double)target));
        }

        AddRow(w => (double)w.Candidate.BaseCalories100g, t.Calories, 3.0);
        AddRow(w => (double)w.Candidate.BaseProtein100g, t.ProteinG, 1.0);
        AddRow(w => (double)w.Candidate.BaseFat100g, t.FatG, 1.0);
        AddRow(w => (double)w.Candidate.BaseCarbs100g, t.CarbsG, 1.0);
        foreach (var s in slots)
        {
            var slot = s;
            AddRow(w => w.Slot.Equals(slot) ? (double)w.Candidate.BaseCalories100g : 0.0, slot.TargetCalories, 1.0);
        }

        int rows = coefs.Count;
        var residual = new double[rows];
        for (int r = 0; r < rows; r++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += coefs[r][i] * u[i];
            residual[r] = sum - rowTargets[r];
        }

        for (int sweep = 0; sweep < 400; sweep++)
        {
            double maxDelta = 0;
            for (int i = 0; i < n; i++)
            {
                double num = 0, den = 0;
                for (int r = 0; r < rows; r++)
                {
                    double a = coefs[r][i];
                    if (a == 0.0) continue;
                    num += weights[r] * a * residual[r];
                    den += weights[r] * a * a;
                }
                if (den <= 0.0) continue;

                double newU = Math.Clamp(u[i] - num / den, lo[i], hi[i]);
                double delta = newU - u[i];
                if (delta == 0.0) continue;

                u[i] = newU;
                for (int r = 0; r < rows; r++) residual[r] += coefs[r][i] * delta;
                maxDelta = Math.Max(maxDelta, Math.Abs(delta));
            }
            if (maxDelta < 1e-6) break;
        }

        for (int i = 0; i < n; i++)
        {
            // округлення до 5 г
            var grams = Math.Round((decimal)(u[i] * 100.0) / 5m) * 5m;
            var (min, max) = GetPortionBounds(work[i].Candidate);
            work[i].Grams = Math.Clamp(grams, min, max);
        }
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

            // Бонус за збалансований склад БЖВ (ціль 30/25/45 % калорій)
            double cal = (double)c.BaseCalories100g;
            if (cal > 0)
            {
                double fp = (double)(c.BaseProtein100g * 4m) / cal;
                double ff = (double)(c.BaseFat100g * 9m) / cal;
                double fc = (double)(c.BaseCarbs100g * 4m) / cal;
                double l1 = Math.Abs(fp - 0.30) + Math.Abs(ff - 0.25) + Math.Abs(fc - 0.45);
                score += 100.0 * (1.0 - Math.Min(l1, 2.0) / 2.0);
            }

            score += Random.Shared.NextDouble() * 50.0;
            scored.Add((c, score));
        }

        var top = scored.OrderByDescending(x => x.Score).Take(3).ToList();
        return top.Count > 0 ? top[Random.Shared.Next(top.Count)].Item : list[Random.Shared.Next(list.Count)];
    }

    public async Task<NutritionPlan?> GetForDateAsync(Guid userId, DateTime date, CancellationToken cancellationToken = default)
    {
        EnsureCanManagePlansFor(userId);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.NutritionPlans
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .Include(p => p.Items).ThenInclude(i => i.Dish)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PlanDate.Date == date.Date, cancellationToken);
    }

    public async Task<IReadOnlyList<NutritionPlan>> GetHistoryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        EnsureCanManagePlansFor(userId);
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
        if (!_session.IsAuthenticated || _session.CurrentUser == null)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));

        if (portionGrams <= 0m)
            throw new ArgumentOutOfRangeException(nameof(portionGrams), _loc.GetString("Err_PositivePortion"));

        if (newProductId.HasValue == newDishId.HasValue)
            throw new ArgumentException("Exactly one replacement item must be specified.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var item = await db.PlanItems
            .Include(x => x.NutritionPlan)
            .SingleOrDefaultAsync(x => x.Id == planItemId, cancellationToken);
        if (item == null) return false;

        if (item.NutritionPlan == null || item.NutritionPlan.UserId != _session.CurrentUser.Id)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));

        NutritionSnapshot nutrition;

        if (newProductId.HasValue)
        {
            var product = await db.Products.FirstOrDefaultAsync(
                p => p.Id == newProductId.Value && p.IsActive && p.IsGlobal, cancellationToken)
                ?? throw new KeyNotFoundException(_loc.GetString("Err_ProductNotFound"));

            if (_session.CurrentUser.Role == UserRole.User &&
                !await db.UserCatalogProducts.AnyAsync(x => x.UserId == _session.CurrentUser.Id && x.ProductId == product.Id, cancellationToken))
            {
                throw new InvalidOperationException(_loc.GetString("Err_ProductNotInPersonalCatalog"));
            }

            nutrition = _calculator.CalculateProductPortion(product, portionGrams);
        }
        else
        {
            var dish = await db.Dishes.Include(d => d.Ingredients).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(d => d.Id == newDishId!.Value && d.IsActive, cancellationToken)
                ?? throw new KeyNotFoundException(_loc.GetString("Err_DishNotFound"));

            if (_session.CurrentUser.Role == UserRole.Admin && !dish.IsGlobal)
                throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));

            if (_session.CurrentUser.Role == UserRole.User && dish.OwnerUserId != null && dish.OwnerUserId != _session.CurrentUser.Id)
                throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));

            var baseNutr = dish.CalculateNutrition();
            nutrition = new NutritionSnapshot(
                baseNutr.Calories * (portionGrams / 100m),
                baseNutr.ProteinG * (portionGrams / 100m),
                baseNutr.FatG * (portionGrams / 100m),
                baseNutr.CarbsG * (portionGrams / 100m));
        }

        item.UpdateFoodItem(newProductId, newDishId, portionGrams, nutrition);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void EnsureCanManagePlansFor(Guid userId)
    {
        if (!_session.IsAuthenticated || _session.CurrentUser is null)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));

        if (_session.CurrentUser.Role != UserRole.Admin && _session.CurrentUser.Id != userId)
            throw new UnauthorizedAccessException(_loc.GetString("Err_Unauthorized"));
    }

    private const int MinMealCount = 1;
    private const int MaxMealCount = 8;

    private static int NormalizeMealCount(int mealCount) =>
        Math.Clamp(mealCount, MinMealCount, MaxMealCount);

    private Queue<MealSlot> BuildMealQueue(int mealCount, decimal totalCalories)
    {
        var queue = new Queue<MealSlot>();
        mealCount = NormalizeMealCount(mealCount);

        var singleMeal = _loc.GetString("Meal_Single");
        var breakfast = _loc.GetString("Meal_Breakfast");
        var lunch = _loc.GetString("Meal_Lunch");
        var dinner = _loc.GetString("Meal_Dinner");
        var snack = _loc.GetString("Meal_Snack");
        var secondBreakfast = _loc.GetString("Meal_SecondBreakfast");
        var lateDinner = _loc.GetString("Meal_LateDinner");
        var snack2 = _loc.GetString("Meal_Snack2");
        var snack3 = _loc.GetString("Meal_Snack3");

        switch (mealCount)
        {
            case 1:
                queue.Enqueue(new MealSlot(MealType.Breakfast, singleMeal, totalCalories));
                break;
            case 2:
                queue.Enqueue(new MealSlot(MealType.Breakfast, breakfast, totalCalories * 0.50m));
                queue.Enqueue(new MealSlot(MealType.Dinner, dinner, totalCalories * 0.50m));
                break;
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
            case 7:
                queue.Enqueue(new MealSlot(MealType.Breakfast, breakfast, totalCalories * 0.18m));
                queue.Enqueue(new MealSlot(MealType.Snack, secondBreakfast, totalCalories * 0.08m));
                queue.Enqueue(new MealSlot(MealType.Lunch, lunch, totalCalories * 0.28m));
                queue.Enqueue(new MealSlot(MealType.Snack, snack, totalCalories * 0.08m));
                queue.Enqueue(new MealSlot(MealType.Dinner, dinner, totalCalories * 0.18m));
                queue.Enqueue(new MealSlot(MealType.Snack, lateDinner, totalCalories * 0.08m));
                queue.Enqueue(new MealSlot(MealType.Snack, snack2, totalCalories * 0.12m));
                break;
            case 8:
                queue.Enqueue(new MealSlot(MealType.Breakfast, breakfast, totalCalories * 0.18m));
                queue.Enqueue(new MealSlot(MealType.Snack, secondBreakfast, totalCalories * 0.08m));
                queue.Enqueue(new MealSlot(MealType.Lunch, lunch, totalCalories * 0.25m));
                queue.Enqueue(new MealSlot(MealType.Snack, snack, totalCalories * 0.08m));
                queue.Enqueue(new MealSlot(MealType.Dinner, dinner, totalCalories * 0.18m));
                queue.Enqueue(new MealSlot(MealType.Snack, lateDinner, totalCalories * 0.08m));
                queue.Enqueue(new MealSlot(MealType.Snack, snack2, totalCalories * 0.07m));
                queue.Enqueue(new MealSlot(MealType.Snack, snack3, totalCalories * 0.08m));
                break;
        }

        return queue;
    }
}