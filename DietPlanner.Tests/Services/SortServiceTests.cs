using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class SortServiceTests
{
    [Fact]
    public void SortProducts_ByCaloriesAscending()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var items = new[]
        {
            TestDataFactory.CreateProduct(category.Id, "High", 300m),
            TestDataFactory.CreateProduct(category.Id, "Low", 100m),
            TestDataFactory.CreateProduct(category.Id, "Medium", 200m)
        };

        var result = new SortService().SortProducts(items, SortField.Calories, false, SortField.Name, false);

        Assert.Equal(["Low", "Medium", "High"], result.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void SortProducts_PrimaryDescendingAndSecondaryAscending()
    {
        var category = TestDataFactory.CreateGlobalCategory();
        var items = new[]
        {
            TestDataFactory.CreateProduct(category.Id, "B", 200m, protein: 10m),
            TestDataFactory.CreateProduct(category.Id, "A", 200m, protein: 20m),
            TestDataFactory.CreateProduct(category.Id, "C", 100m, protein: 30m)
        };

        var result = new SortService().SortProducts(items, SortField.Calories, true, SortField.Protein, false);

        Assert.Equal(["B", "A", "C"], result.Select(x => x.Name).ToArray());
    }
}
