namespace DietPlanner.Models;

public abstract class DietaryRestriction
{
    protected DietaryRestriction()
    {
    }

    protected DietaryRestriction(string name, string description)
    {
        Id = Guid.NewGuid();
        Name = name.Trim();
        Description = description.Trim();
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public abstract bool Allows(Product product);
}

public sealed class AllergenRestriction : DietaryRestriction
{
    private AllergenRestriction()
    {
    }

    public AllergenRestriction(string allergenName, string? description = null)
        : base($"Allergen: {allergenName}", description ?? $"Excludes products containing {allergenName}.")
    {
        AllergenName = allergenName.Trim();
    }

    public string AllergenName { get; private set; } = string.Empty;

    public override bool Allows(Product product) =>
        !product.Allergens.Contains(AllergenName, StringComparer.OrdinalIgnoreCase);
}

public sealed class ForbiddenProductRestriction : DietaryRestriction
{
    private ForbiddenProductRestriction()
    {
    }

    public ForbiddenProductRestriction(Guid productId, string productName)
        : base($"Forbidden: {productName}", $"Excludes the selected product.")
    {
        ProductId = productId;
    }

    public Guid ProductId { get; private set; }

    public override bool Allows(Product product) => product.Id != ProductId;
}

public sealed class DietModeRestriction : DietaryRestriction
{
    private DietModeRestriction()
    {
    }

    public DietModeRestriction(string modeName, IEnumerable<string> excludedTags, string? description = null)
        : base(modeName, description ?? $"Dietary mode: {modeName}.")
    {
        ModeName = modeName.Trim();
        ExcludedTagsJson = System.Text.Json.JsonSerializer.Serialize(
            excludedTags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    public string ModeName { get; private set; } = string.Empty;
    public string ExcludedTagsJson { get; private set; } = "[]";

    public override bool Allows(Product product)
    {
        var excluded = System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(ExcludedTagsJson)
                       ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return !excluded.Overlaps(product.DietaryTags);
    }
}

public sealed class UserRestriction
{
    private UserRestriction()
    {
    }

    public UserRestriction(Guid userId, Guid dietaryRestrictionId)
    {
        UserId = userId;
        DietaryRestrictionId = dietaryRestrictionId;
    }

    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public Guid DietaryRestrictionId { get; private set; }
    public DietaryRestriction? DietaryRestriction { get; private set; }
}
