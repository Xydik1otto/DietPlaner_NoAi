using DietPlanner.Models;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Models;

public sealed class ProductTests
{
    [Fact]
    public void CalculateNutritionSnapshot_ScalesProportionally()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var product = TestDataFactory.CreateProduct(category.Id, calories: 200m, protein: 10m, fat: 8m, carbs: 20m);

        var snapshot = product.CalculateNutritionSnapshot(50m);

        Assert.Equal(100m, snapshot.Calories);
        Assert.Equal(5m, snapshot.ProteinG);
        Assert.Equal(4m, snapshot.FatG);
        Assert.Equal(10m, snapshot.CarbsG);
    }

    [Fact]
    public void CalculateNutritionSnapshot_RejectsNonPositiveAmount()
    {
        var product = TestDataFactory.CreateProduct(TestDataFactory.CreateGlobalCategory().Id);

        Assert.Throws<ArgumentOutOfRangeException>(() => product.CalculateNutritionSnapshot(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => product.CalculateNutritionSnapshot(-1m));
    }

    [Fact]
    public void SetAllergensAndTags_NormalizeAndPersist()
    {
        var product = TestDataFactory.CreateProduct(TestDataFactory.CreateGlobalCategory().Id);

        product.SetAllergens([" milk ", "Milk", "eggs"]);
        product.SetDietaryTags([" high-protein ", "high-protein"]);

        Assert.Equal(2, product.Allergens.Count);
        Assert.Contains("milk", product.Allergens);
        Assert.Contains("eggs", product.Allergens);
        Assert.Single(product.DietaryTags);
        Assert.Contains("high-protein", product.DietaryTags);
    }
}
