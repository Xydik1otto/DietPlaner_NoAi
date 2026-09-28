namespace DietPlanner.Models;

public sealed class Category
{
    private string _name = string.Empty;

    private Category()
    {
    }

    public Category(string name, string? description = null)
    {
        Id = Guid.NewGuid();
        SetName(name);
        SetDescription(description);
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string Name => _name;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public ICollection<Product> Products { get; private set; } = new List<Product>();
    public ICollection<Dish> Dishes { get; private set; } = new List<Dish>();

    public void SetName(string name)
    {
        _name = name.Trim();
        Touch();
    }

    public void SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
