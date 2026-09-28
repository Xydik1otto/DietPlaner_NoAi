using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public enum SortField
{
    Category = 0,
    Calories = 1,
    Protein = 2,
    Fat = 3,
    Carbohydrates = 4,
    Name = 5
}

public interface ISortService
{
    IReadOnlyList<Product> SortProducts(IEnumerable<Product> source, SortField primary, bool primaryDescending, SortField secondary, bool secondaryDescending);
}
