namespace DietPlanner.Models;

public sealed class UserCatalogProduct
{
    private UserCatalogProduct()
    {
    }

    public UserCatalogProduct(Guid userId, Guid productId)
    {
        UserId = userId;
        ProductId = productId;
        AddedAtUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public Guid ProductId { get; private set; }
    public Product? Product { get; private set; }
    public DateTime AddedAtUtc { get; private set; }
}
