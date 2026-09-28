using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class SortService : ISortService
{
    public IReadOnlyList<Product> SortProducts(IEnumerable<Product> source, SortField primary, bool primaryDescending, SortField secondary, bool secondaryDescending)
    {
        var items = source.ToList();
        // Deliberate custom insertion sort. No LINQ OrderBy is used for the required criterion.
        for (var i = 1; i < items.Count; i++)
        {
            var current = items[i];
            var j = i - 1;
            while (j >= 0 && Compare(items[j], current, primary, primaryDescending, secondary, secondaryDescending) > 0)
            {
                items[j + 1] = items[j];
                j--;
            }
            items[j + 1] = current;
        }
        return items;
    }

    private static int Compare(Product left, Product right, SortField primary, bool primaryDescending, SortField secondary, bool secondaryDescending)
    {
        var first = CompareField(left, right, primary);
        if (first != 0) return primaryDescending ? -first : first;
        var second = CompareField(left, right, secondary);
        if (second != 0) return secondaryDescending ? -second : second;
        return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int CompareField(Product left, Product right, SortField field) => field switch
    {
        SortField.Category => string.Compare(left.Category?.Name, right.Category?.Name, StringComparison.CurrentCultureIgnoreCase),
        SortField.Calories => left.Calories.CompareTo(right.Calories),
        SortField.Protein => left.ProteinG.CompareTo(right.ProteinG),
        SortField.Fat => left.FatG.CompareTo(right.FatG),
        SortField.Carbohydrates => left.CarbsG.CompareTo(right.CarbsG),
        SortField.Name => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase),
        _ => 0
    };
}
