namespace DietPlanner.Models;

public sealed class DishIngredient
{
    private DishIngredient()
    {
    }

    public DishIngredient(Guid productId, decimal amount, string unit)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        SetAmount(amount);
        SetUnit(unit);
    }

    public Guid Id { get; private set; }
    public Guid DishId { get; private set; }
    public Dish? Dish { get; private set; }
    public Guid ProductId { get; private set; }
    public Product? Product { get; private set; }
    public decimal Amount { get; private set; }
    public string Unit { get; private set; } = string.Empty;

    public void SetAmount(decimal amount) => Amount = amount;
    public void SetUnit(string unit) => Unit = unit.Trim();
}
