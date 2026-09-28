namespace DietPlanner.Models;

public sealed class Dish
{
    private string _name = string.Empty;

    private Dish()
    {
    }

    public Dish(string name, Guid categoryId, string? description = null)
    {
        Id = Guid.NewGuid();
        SetName(name);
        CategoryId = categoryId;
        SetDescription(description);
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string Name => _name;
    public Guid CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public ICollection<DishIngredient> Ingredients { get; private set; } = new List<DishIngredient>();

    public void SetName(string name)
    {
        _name = name.Trim();
        Touch();
    }

    public void SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
    }

    public void SetCategory(Guid categoryId)
    {
        CategoryId = categoryId;
        Touch();
    }

    public void ReplaceIngredients(IEnumerable<DishIngredient> ingredients)
    {
        Ingredients.Clear();
        foreach (var ingredient in ingredients)
        {
            Ingredients.Add(ingredient);
        }
        Touch();
    }

    public NutritionSnapshot CalculateNutrition()
    {
        var total = new NutritionSnapshot(0m, 0m, 0m, 0m);
        foreach (var ingredient in Ingredients)
        {
            var nutrition = ingredient.Product?.CalculateNutritionSnapshot(ingredient.Amount)
                ?? throw new InvalidOperationException("Dish ingredient product is not loaded.");

            total = new NutritionSnapshot(
                total.Calories + nutrition.Calories,
                total.ProteinG + nutrition.ProteinG,
                total.FatG + nutrition.FatG,
                total.CarbsG + nutrition.CarbsG);
        }

        return total;
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
