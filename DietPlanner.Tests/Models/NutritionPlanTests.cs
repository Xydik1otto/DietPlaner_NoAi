using DietPlanner.Models;

namespace DietPlanner.Tests.Models;

public sealed class NutritionPlanTests
{
    [Fact]
    public void CalculateActualTotals_SumsPlanItems()
    {
        var plan = new NutritionPlan(
            Guid.NewGuid(),
            DateTime.UtcNow,
            2,
            new NutritionTargets(500m, 30m, 15m, 50m));

        plan.Items.Add(new PlanItem(MealType.Breakfast, "Breakfast", Guid.NewGuid(), null, 100m, "g", new NutritionSnapshot(200m, 10m, 5m, 20m)));
        plan.Items.Add(new PlanItem(MealType.Dinner, "Dinner", Guid.NewGuid(), null, 150m, "g", new NutritionSnapshot(250m, 15m, 7m, 25m)));

        var totals = plan.CalculateActualTotals();

        Assert.Equal(450m, totals.Calories);
        Assert.Equal(25m, totals.ProteinG);
        Assert.Equal(12m, totals.FatG);
        Assert.Equal(45m, totals.CarbsG);
    }

    [Fact]
    public void CalculateDeviation_IsActualMinusTarget()
    {
        var plan = new NutritionPlan(Guid.NewGuid(), DateTime.UtcNow, 1, new NutritionTargets(500m, 30m, 15m, 50m));
        plan.Items.Add(new PlanItem(MealType.Breakfast, "Breakfast", null, null, 100m, "g", new NutritionSnapshot(550m, 32m, 14m, 60m)));

        var deviation = plan.CalculateDeviation();

        Assert.Equal(50m, deviation.Calories);
        Assert.Equal(2m, deviation.ProteinG);
        Assert.Equal(-1m, deviation.FatG);
        Assert.Equal(10m, deviation.CarbsG);
    }
    [Fact]
    public void OneMealCanContainMultiplePlanItemsWithoutChangingMealCount()
    {
        var plan = new NutritionPlan(
            Guid.NewGuid(),
            DateTime.UtcNow,
            1,
            new NutritionTargets(500m, 30m, 15m, 50m));

        plan.Items.Add(new PlanItem(MealType.Lunch, "Lunch", Guid.NewGuid(), null, 100m, "g", new NutritionSnapshot(200m, 10m, 5m, 20m)));
        plan.Items.Add(new PlanItem(MealType.Lunch, "Lunch", Guid.NewGuid(), null, 100m, "g", new NutritionSnapshot(150m, 8m, 3m, 15m)));
        plan.Items.Add(new PlanItem(MealType.Lunch, "Lunch", Guid.NewGuid(), null, 80m, "g", new NutritionSnapshot(100m, 3m, 1m, 10m)));

        Assert.Equal(1, plan.MealCount);
        Assert.Equal(1, plan.Items.Select(item => item.MealName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, plan.Items.Count);
    }

}
