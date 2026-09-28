namespace DietPlanner.Models;

public enum UserRole
{
    User = 0,
    Admin = 1
}

public enum Sex
{
    Male = 0,
    Female = 1
}

public enum ActivityLevel
{
    Sedentary = 0,
    Light = 1,
    Moderate = 2,
    High = 3,
    VeryHigh = 4
}

public enum NutritionGoal
{
    MaintainWeight = 0,
    LoseWeight = 1,
    GainWeight = 2,
    Recompose = 3
}

public enum NutritionBasis
{
    Per100Grams = 0,
    Per100Milliliters = 1,
    PerPiece = 2
}

public enum MealType
{
    Breakfast = 0,
    Lunch = 1,
    Dinner = 2,
    Snack = 3,
    Custom = 4
}

public enum MealEntrySource
{
    Product = 0,
    Dish = 1,
    Manual = 2
}

public enum ActionType
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Restore = 3,
    CreatePlan = 4,
    ReplaceMeal = 5,
    AddMealIntake = 6,
    ExportReport = 7,
    Login = 8,
    Register = 9,
    Logout = 10,
    Error = 11
}
