namespace DietPlanner.Models;

public sealed class NutritionPlan
{
    private NutritionPlan()
    {
    }

    public NutritionPlan(Guid userId, DateTime planDate, int mealCount, NutritionTargets targets)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        PlanDate = planDate.Date;
        MealCount = mealCount;
        TargetCalories = targets.Calories;
        TargetProteinG = targets.ProteinG;
        TargetFatG = targets.FatG;
        TargetCarbsG = targets.CarbsG;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public DateTime PlanDate { get; private set; }
    public int MealCount { get; private set; }

    public decimal TargetCalories { get; private set; }
    public decimal TargetProteinG { get; private set; }
    public decimal TargetFatG { get; private set; }
    public decimal TargetCarbsG { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public ICollection<PlanItem> Items { get; private set; } = new List<PlanItem>();

    public NutritionSnapshot CalculateActualTotals()
    {
        var total = new NutritionSnapshot(0m, 0m, 0m, 0m);
        foreach (var item in Items)
        {
            total = new NutritionSnapshot(
                total.Calories + item.Calories,
                total.ProteinG + item.ProteinG,
                total.FatG + item.FatG,
                total.CarbsG + item.CarbsG);
        }
        return total;
    }

    public NutritionDeviation CalculateDeviation()
    {
        var actual = CalculateActualTotals();
        return new NutritionDeviation(
            actual.Calories - TargetCalories,
            actual.ProteinG - TargetProteinG,
            actual.FatG - TargetFatG,
            actual.CarbsG - TargetCarbsG);
    }
}

public sealed class PlanItem
{
    private PlanItem()
    {
    }

    public PlanItem(
        MealType mealType,
        string mealName,
        Guid? productId,
        Guid? dishId,
        decimal portionAmount,
        string portionUnit,
        NutritionSnapshot nutrition)
    {
        Id = Guid.NewGuid();
        MealType = mealType;
        MealName = mealName.Trim();
        ProductId = productId;
        DishId = dishId;
        PortionAmount = portionAmount;
        PortionUnit = portionUnit.Trim();
        Calories = nutrition.Calories;
        ProteinG = nutrition.ProteinG;
        FatG = nutrition.FatG;
        CarbsG = nutrition.CarbsG;
    }

    public Guid Id { get; private set; }
    public Guid NutritionPlanId { get; private set; }
    public NutritionPlan? NutritionPlan { get; private set; }
    public MealType MealType { get; private set; }
    public string MealName { get; private set; } = string.Empty;
    public Guid? ProductId { get; private set; }
    public Product? Product { get; private set; }
    public Guid? DishId { get; private set; }
    public Dish? Dish { get; private set; }
    public decimal PortionAmount { get; private set; }
    public string PortionUnit { get; private set; } = string.Empty;
    public decimal Calories { get; private set; }
    public decimal ProteinG { get; private set; }
    public decimal FatG { get; private set; }
    public decimal CarbsG { get; private set; }

    public void UpdateFoodItem(Guid? productId, Guid? dishId, decimal portionAmount, NutritionSnapshot nutrition)
    {
        ProductId = productId;
        DishId = dishId;
        PortionAmount = portionAmount;
        Calories = nutrition.Calories;
        ProteinG = nutrition.ProteinG;
        FatG = nutrition.FatG;
        CarbsG = nutrition.CarbsG;
    }
}

public readonly record struct NutritionTargets(
    decimal Calories,
    decimal ProteinG,
    decimal FatG,
    decimal CarbsG);

public readonly record struct NutritionDeviation(
    decimal Calories,
    decimal ProteinG,
    decimal FatG,
    decimal CarbsG);