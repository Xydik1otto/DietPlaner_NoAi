using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class NutritionCalculatorBoundaryTests
{
    [Fact]
    public void Calculate_AcceptsExactly18YearsOld()
    {
        var user = CreateValidUser(DateTime.UtcNow.Date.AddYears(-18));
        var calculator = new NutritionCalculator(new StubLocalizationService());

        var result = calculator.Calculate(user);

        Assert.Equal(18, result.AgeYears);
    }

    [Fact]
    public void Calculate_AcceptsExactly120YearsOld()
    {
        var user = CreateValidUser(DateTime.UtcNow.Date.AddYears(-120));
        var calculator = new NutritionCalculator(new StubLocalizationService());

        var result = calculator.Calculate(user);

        Assert.Equal(120, result.AgeYears);
    }

    [Fact]
    public void Calculate_Rejects17YearsOld()
    {
        var user = CreateValidUser(DateTime.UtcNow.Date.AddYears(-17));
        var calculator = new NutritionCalculator(new StubLocalizationService());

        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(user));
    }

    [Fact]
    public void Calculate_RejectsOlderThan120()
    {
        var user = CreateValidUser(DateTime.UtcNow.Date.AddYears(-121));
        var calculator = new NutritionCalculator(new StubLocalizationService());

        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(user));
    }

    [Fact]
    public void Calculate_RejectsZeroOrNegativeHeightAndWeight()
    {
        var calculator = new NutritionCalculator(new StubLocalizationService());
        var zeroHeight = CreateValidUser();
        zeroHeight.UpdateNutritionProfile(zeroHeight.BirthDate, Sex.Male, 0m, 75m, ActivityLevel.Moderate, NutritionGoal.MaintainWeight, null, null);
        var negativeWeight = CreateValidUser();
        negativeWeight.UpdateNutritionProfile(negativeWeight.BirthDate, Sex.Male, 180m, -1m, ActivityLevel.Moderate, NutritionGoal.MaintainWeight, null, null);

        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(zeroHeight));
        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.Calculate(negativeWeight));
    }

    [Fact]
    public void Calculate_ClampsVeryLowTargetCaloriesTo1200()
    {
        var calculator = new NutritionCalculator(new StubLocalizationService());
        var user = CreateValidUser();
        user.UpdateNutritionProfile(
            user.BirthDate,
            Sex.Female,
            50m,
            20m,
            ActivityLevel.Sedentary,
            NutritionGoal.LoseWeight,
            null,
            null);

        var result = calculator.Calculate(user);

        Assert.Equal(1200m, result.TargetCalories);
    }

    private static User CreateValidUser(DateTime? birthDate = null)
    {
        var user = new User("boundary@example.com", "Boundary", "hash", UserRole.User);
        user.UpdateNutritionProfile(
            birthDate ?? DateTime.UtcNow.Date.AddYears(-25),
            Sex.Male,
            180m,
            75m,
            ActivityLevel.Moderate,
            NutritionGoal.MaintainWeight,
            null,
            null);
        return user;
    }
}
