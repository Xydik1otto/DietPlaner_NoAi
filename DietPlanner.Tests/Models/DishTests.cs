using DietPlanner.Models;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Models;

public sealed class DishTests
{
    [Fact]
    public void CalculateNutrition_SumsAllIngredients()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var chicken = TestDataFactory.CreateProduct(category.Id, "Chicken", 200m, 20m, 10m, 0m);
        var rice = TestDataFactory.CreateProduct(category.Id, "Rice", 100m, 2m, 1m, 20m);
        var dish = new Dish("Chicken Rice", category.Id);

        var chickenIngredient = new DishIngredient(chicken.Id, 150m, "g");
        var riceIngredient = new DishIngredient(rice.Id, 200m, "g");
        typeof(DishIngredient).GetProperty(nameof(DishIngredient.Product))!.SetValue(chickenIngredient, chicken);
        typeof(DishIngredient).GetProperty(nameof(DishIngredient.Product))!.SetValue(riceIngredient, rice);
        dish.Ingredients.Add(chickenIngredient);
        dish.Ingredients.Add(riceIngredient);

        var result = dish.CalculateNutrition();

        Assert.Equal(500m, result.Calories);
        Assert.Equal(34m, result.ProteinG);
        Assert.Equal(16m, result.FatG);
        Assert.Equal(40m, result.CarbsG);
    }
}
