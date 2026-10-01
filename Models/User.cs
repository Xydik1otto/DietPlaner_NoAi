namespace DietPlanner.Models;

public sealed class User
{
    private string _email = string.Empty;
    private string _displayName = string.Empty;

    private User()
    {
    }

    public User(string email, string displayName, string passwordHash, UserRole role)
    {
        Id = Guid.NewGuid();
        SetEmail(email);
        SetDisplayName(displayName);
        PasswordHash = passwordHash;
        Role = role;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }
    public string Email => _email;
    public string PasswordHash { get; private set; } = string.Empty;
    public string DisplayName => _displayName;
    public UserRole Role { get; private set; }

    public DateTime? BirthDate { get; private set; }
    public Sex? SexForCalculation { get; private set; }
    public decimal? HeightCm { get; private set; }
    public decimal? WeightKg { get; private set; }
    public ActivityLevel? ActivityLevel { get; private set; }
    public NutritionGoal? Goal { get; private set; }
    public string? HealthConditionLabel { get; private set; }
    public string? HealthNotes { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }

    public ICollection<UserRestriction> Restrictions { get; private set; } = new List<UserRestriction>();
    public ICollection<MealIntake> MealIntakes { get; private set; } = new List<MealIntake>();
    public ICollection<NutritionPlan> Plans { get; private set; } = new List<NutritionPlan>();
    public ICollection<PlanHistory> PlanHistory { get; private set; } = new List<PlanHistory>();
    public ICollection<ActionRecord> ActionRecords { get; private set; } = new List<ActionRecord>();
    public ICollection<UserCatalogProduct> CatalogProducts { get; private set; } = new List<UserCatalogProduct>();

    public void SetEmail(string email)
    {
        _email = email.Trim();
    }

    public void SetDisplayName(string displayName)
    {
        _displayName = displayName.Trim();
    }

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        Touch();
    }

    public void SetRole(UserRole role)
    {
        Role = role;
        Touch();
    }

    public void UpdateNutritionProfile(
        DateTime? birthDate,
        Sex? sex,
        decimal? heightCm,
        decimal? weightKg,
        ActivityLevel? activityLevel,
        NutritionGoal? goal,
        string? healthConditionLabel,
        string? healthNotes)
    {
        BirthDate = birthDate;
        SexForCalculation = sex;
        HeightCm = heightCm;
        WeightKg = weightKg;
        ActivityLevel = activityLevel;
        Goal = goal;
        HealthConditionLabel = string.IsNullOrWhiteSpace(healthConditionLabel) ? null : healthConditionLabel.Trim();
        HealthNotes = string.IsNullOrWhiteSpace(healthNotes) ? null : healthNotes.Trim();
        Touch();
    }

    public void MarkLogin()
    {
        LastLoginAtUtc = DateTime.UtcNow;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
