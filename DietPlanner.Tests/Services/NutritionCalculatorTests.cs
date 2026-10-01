using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class NutritionCalculatorTests
{
    [Fact]
    public void Calculate_RejectsIncompleteProfile()
    {
        var service = new NutritionCalculator(new StubLocalizationService());
        var user = new User("test@example.com", "Test", "hash", UserRole.User);

        Assert.Throws<InvalidOperationException>(() => service.Calculate(user));
    }

    [Fact]
    public void Calculate_ProducesTargetsAndBmi()
    {
        var service = new NutritionCalculator(new StubLocalizationService());
        var user = TestDataFactory.CreateUser();

        var result = service.Calculate(user);

        Assert.True(result.Targets.Calories >= 1200m);
        Assert.True(result.BodyMassIndex > 0m);
        Assert.InRange(Math.Abs(result.TargetCalories * 0.30m / 4m - result.Targets.ProteinG), 0m, 0.1m);
        Assert.InRange(Math.Abs(result.TargetCalories * 0.45m / 4m - result.Targets.CarbsG), 0m, 0.1m);
    }

    [Fact]
    public void CalculateProductPortion_DelegatesToProductScaling()
    {
        var service = new NutritionCalculator(new StubLocalizationService());
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, calories: 200m, protein: 10m, fat: 8m, carbs: 20m);

        var snapshot = service.CalculateProductPortion(product, 50m);

        Assert.Equal(100m, snapshot.Calories);
        Assert.Equal(5m, snapshot.ProteinG);
    }

    [Fact]
    public void CalculateDishPortion_RejectsNonPositiveMultiplier()
    {
        var service = new NutritionCalculator(new StubLocalizationService());
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, "Rice", 100m, 2m, 1m, 20m);
        var dish = new Dish("Rice", category.Id);
        var ingredient = new DishIngredient(product.Id, 100m, "g");
        typeof(DishIngredient).GetProperty(nameof(DishIngredient.Product))!.SetValue(ingredient, product);
        dish.Ingredients.Add(ingredient);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.CalculateDishPortion(dish, 0m));
    }
}
