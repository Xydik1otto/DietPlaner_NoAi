namespace DietPlanner.Models;

public sealed class MealIntake
{
    private MealIntake()
    {
    }

    public MealIntake(Guid userId, DateTime consumedAtUtc, MealType mealType, string? name = null)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ConsumedAtUtc = consumedAtUtc;
        MealType = mealType;
        Name = string.IsNullOrWhiteSpace(name) ? mealType.ToString() : name.Trim();
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public DateTime ConsumedAtUtc { get; private set; }
    public MealType MealType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    

    public ICollection<MealIntakeItem> Items { get; private set; } = new List<MealIntakeItem>();

    public NutritionSnapshot CalculateTotals()
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
}

public sealed class MealIntakeItem
{
    private MealIntakeItem()
    {
    }

    public MealIntakeItem(
        MealEntrySource source,
        Guid? productId,
        Guid? dishId,
        string itemName,
        decimal amount,
        string unit,
        NutritionSnapshot nutrition)
    {
        Id = Guid.NewGuid();
        Source = source;
        ProductId = productId;
        DishId = dishId;
        ItemName = itemName.Trim();
        Amount = amount;
        Unit = unit.Trim();
        Calories = nutrition.Calories;
        ProteinG = nutrition.ProteinG;
        FatG = nutrition.FatG;
        CarbsG = nutrition.CarbsG;
    }

    public Guid Id { get; private set; }
    public Guid MealIntakeId { get; private set; }
    public MealIntake? MealIntake { get; private set; }
    public MealEntrySource Source { get; private set; }
    public Guid? ProductId { get; private set; }
    public Product? Product { get; private set; }
    public Guid? DishId { get; private set; }
    public Dish? Dish { get; private set; }
    public string ItemName { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal Calories { get; private set; }
    public decimal ProteinG { get; private set; }
    public decimal FatG { get; private set; }
    public decimal CarbsG { get; private set; }
}
