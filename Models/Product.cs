using System.Text.Json;

namespace DietPlanner.Models;

public sealed class Product
{
    private string _name = string.Empty;
    private HashSet<string> _allergens = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _dietaryTags = new(StringComparer.OrdinalIgnoreCase);

    private Product()
    {
    }

    public Product(
        string name,
        Guid categoryId,
        NutritionBasis basis,
        decimal calories,
        decimal proteinG,
        decimal fatG,
        decimal carbsG)
    {
        Id = Guid.NewGuid();
        SetName(name);
        CategoryId = categoryId;
        Basis = basis;
        ReferenceAmount = basis == NutritionBasis.PerPiece ? 1m : 100m;
        SetNutrition(calories, proteinG, fatG, carbsG);
        IsActive = true;
        IsGlobal = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string Name => _name;
    public Guid CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public string? Description { get; private set; }
    public NutritionBasis Basis { get; private set; }
    public decimal ReferenceAmount { get; private set; }
    public decimal Calories { get; private set; }
    public decimal ProteinG { get; private set; }
    public decimal FatG { get; private set; }
    public decimal CarbsG { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsGlobal { get; private set; }

    public string AllergensJson { get; private set; } = "[]";
    public string DietaryTagsJson { get; private set; } = "[]";

    public IReadOnlySet<string> Allergens => _allergens;
    public IReadOnlySet<string> DietaryTags => _dietaryTags;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public ICollection<DishIngredient> DishIngredients { get; private set; } = new List<DishIngredient>();
    public ICollection<UserCatalogProduct> UserCatalogItems { get; private set; } = new List<UserCatalogProduct>();

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

    public void SetCategory(Guid categoryId)
    {
        CategoryId = categoryId;
        Touch();
    }

    public void SetNutrition(decimal calories, decimal proteinG, decimal fatG, decimal carbsG)
    {
        Calories = calories;
        ProteinG = proteinG;
        FatG = fatG;
        CarbsG = carbsG;
        Touch();
    }

    public void SetReferenceAmount(decimal amount)
    {
        ReferenceAmount = amount;
        Touch();
    }

    public void SetBasis(NutritionBasis basis)
    {
        Basis = basis;
        Touch();
    }

    public void SetAllergens(IEnumerable<string> allergens)
    {
        _allergens = NormalizeSet(allergens);
        AllergensJson = JsonSerializer.Serialize(_allergens.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        Touch();
    }

    public void SetDietaryTags(IEnumerable<string> tags)
    {
        _dietaryTags = NormalizeSet(tags);
        DietaryTagsJson = JsonSerializer.Serialize(_dietaryTags.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
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

    public NutritionSnapshot CalculateNutritionSnapshot(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        if (ReferenceAmount <= 0m)
        {
            throw new InvalidOperationException("Product reference amount must be greater than zero.");
        }

        var multiplier = amount / ReferenceAmount;
        return new NutritionSnapshot(
            Calories * multiplier,
            ProteinG * multiplier,
            FatG * multiplier,
            CarbsG * multiplier);
    }

    public void RestoreCollectionsFromStorage()
    {
        _allergens = DeserializeSet(AllergensJson);
        _dietaryTags = DeserializeSet(DietaryTagsJson);
    }

    private static HashSet<string> NormalizeSet(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> DeserializeSet(string json)
    {
        try
        {
            return DeserializeSetUnsafe(json);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static HashSet<string> DeserializeSetUnsafe(string json)
    {
        var values = JsonSerializer.Deserialize<IEnumerable<string>>(json) ?? Array.Empty<string>();
        return NormalizeSet(values);
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

public readonly record struct NutritionSnapshot(
    decimal Calories,
    decimal ProteinG,
    decimal FatG,
    decimal CarbsG);
