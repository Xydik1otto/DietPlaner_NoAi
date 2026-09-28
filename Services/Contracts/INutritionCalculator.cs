using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public readonly record struct NutritionCalculation(
    int AgeYears,
    decimal RestingEnergyKcal,
    decimal MaintenanceCalories,
    decimal TargetCalories,
    NutritionTargets Targets,
    decimal BodyMassIndex);

public interface INutritionCalculator
{
    NutritionCalculation Calculate(User user);
    NutritionSnapshot CalculateProductPortion(Product product, decimal amount);
    NutritionSnapshot CalculateDishPortion(Dish dish, decimal portionMultiplier);
}
