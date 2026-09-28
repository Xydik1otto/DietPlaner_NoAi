namespace DietPlanner.ViewModels;

public class MealIntakeDisplayDto
{
    public Guid Id { get; set; }
    public DateTime IntakeTime { get; set; } = DateTime.Now;
    public string ItemName { get; set; } = string.Empty;
    public double WeightGrams { get; set; }
    public double Calories { get; set; }
    public double Proteins { get; set; }
    public double Fats { get; set; }
    public double Carbs { get; set; }
    public string NutritionSummary { get; set; } = string.Empty;

    // Додані поля для точної ідентифікації при перевірці стану чекбоксів
    public Guid? ProductId { get; set; }
    public Guid? DishId { get; set; }
}