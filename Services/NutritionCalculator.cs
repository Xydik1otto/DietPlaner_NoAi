using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class NutritionCalculator : INutritionCalculator
{
    private static readonly IReadOnlyDictionary<ActivityLevel, decimal> ActivityFactors =
        new Dictionary<ActivityLevel, decimal>
        {
            [ActivityLevel.Sedentary] = 1.20m,
            [ActivityLevel.Light] = 1.375m,
            [ActivityLevel.Moderate] = 1.55m,
            [ActivityLevel.High] = 1.725m,
            [ActivityLevel.VeryHigh] = 1.90m
        };

    private const decimal DefaultProteinPercent = 30m;
    private const decimal DefaultFatPercent = 25m;
    private const decimal DefaultCarbsPercent = 45m;

    public NutritionCalculation Calculate(User user)
    {
        if (user.BirthDate is null || user.SexForCalculation is null || user.HeightCm is null || user.WeightKg is null || user.ActivityLevel is null || user.Goal is null)
            throw new InvalidOperationException("Заповніть дату народження, стать, зріст, масу, активність та ціль у профілі.");

        var age = CalculateAge(user.BirthDate.Value.Date, DateTime.UtcNow.Date);
        if (age < 18 || age > 120)
            throw new ArgumentOutOfRangeException(nameof(user), "Калькулятор призначений для дорослого користувача віком 18–120 років.");

        var weight = user.WeightKg.Value;
        var height = user.HeightCm.Value;
        if (weight <= 0m || height <= 0m)
            throw new ArgumentOutOfRangeException(nameof(user), "Зріст і маса мають бути більшими за нуль.");

        // Mifflin–St Jeor: RMR = 9.99*kg + 6.25*cm - 4.92*age + sex constant.
        var resting = 9.99m * weight + 6.25m * height - 4.92m * age +
                      (user.SexForCalculation.Value == Sex.Male ? 5m : -161m);

        var maintenance = resting * ActivityFactors[user.ActivityLevel.Value];
        var target = maintenance * GetGoalFactor(user.Goal.Value);
        target = Math.Max(1200m, decimal.Round(target, 0, MidpointRounding.AwayFromZero));

        var protein = target * DefaultProteinPercent / 100m / 4m;
        var fat = target * DefaultFatPercent / 100m / 9m;
        var carbs = target * DefaultCarbsPercent / 100m / 4m;
        var bmi = weight / ((height / 100m) * (height / 100m));

        return new NutritionCalculation(
            age,
            decimal.Round(resting, 0),
            decimal.Round(maintenance, 0),
            target,
            new NutritionTargets(decimal.Round(target, 0), decimal.Round(protein, 1), decimal.Round(fat, 1), decimal.Round(carbs, 1)),
            decimal.Round(bmi, 1));
    }

    public NutritionSnapshot CalculateProductPortion(Product product, decimal amount) =>
        product.CalculateNutritionSnapshot(amount);

    public NutritionSnapshot CalculateDishPortion(Dish dish, decimal portionMultiplier)
    {
        if (portionMultiplier <= 0m)
            throw new ArgumentOutOfRangeException(nameof(portionMultiplier), "Множник порції має бути більшим за нуль.");

        var nutrition = dish.CalculateNutrition();
        return new NutritionSnapshot(
            nutrition.Calories * portionMultiplier,
            nutrition.ProteinG * portionMultiplier,
            nutrition.FatG * portionMultiplier,
            nutrition.CarbsG * portionMultiplier);
    }

    private static int CalculateAge(DateTime birthDate, DateTime today)
    {
        var age = today.Year - birthDate.Year;
        if (birthDate.Date > today.AddYears(-age)) age--;
        return age;
    }

    private static decimal GetGoalFactor(NutritionGoal goal) => goal switch
    {
        NutritionGoal.MaintainWeight => 1.00m,
        NutritionGoal.LoseWeight => 0.85m,
        NutritionGoal.GainWeight => 1.10m,
        NutritionGoal.Recompose => 0.95m,
        _ => 1.00m
    };
}
