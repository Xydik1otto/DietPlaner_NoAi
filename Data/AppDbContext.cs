using DietPlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Dish> Dishes => Set<Dish>();
    public DbSet<DishIngredient> DishIngredients => Set<DishIngredient>();
    public DbSet<UserCatalogProduct> UserCatalogProducts => Set<UserCatalogProduct>();
    public DbSet<DietaryRestriction> DietaryRestrictions => Set<DietaryRestriction>();
    public DbSet<UserRestriction> UserRestrictions => Set<UserRestriction>();
    public DbSet<MealIntake> MealIntakes => Set<MealIntake>();
    public DbSet<MealIntakeItem> MealIntakeItems => Set<MealIntakeItem>();
    public DbSet<NutritionPlan> NutritionPlans => Set<NutritionPlan>();
    public DbSet<PlanItem> PlanItems => Set<PlanItem>();
    public DbSet<PlanHistory> PlanHistories => Set<PlanHistory>();
    public DbSet<ActionRecord> ActionRecords => Set<ActionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUsers(modelBuilder);
        ConfigureCategories(modelBuilder);
        ConfigureProducts(modelBuilder);
        ConfigureDishes(modelBuilder);
        ConfigureUserCatalogProducts(modelBuilder);
        ConfigureRestrictions(modelBuilder);
        ConfigureMealIntakes(modelBuilder);
        ConfigurePlans(modelBuilder);
        ConfigurePlanHistory(modelBuilder);
        ConfigureActionRecords(modelBuilder);
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<User>();
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Email).HasMaxLength(320).HasField("_email").IsRequired();
        entity.HasIndex(x => x.Email).IsUnique();
        entity.Property(x => x.DisplayName).HasMaxLength(120).HasField("_displayName").IsRequired();
        entity.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
        entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        entity.Property(x => x.HeightCm).HasPrecision(6, 2);
        entity.Property(x => x.WeightKg).HasPrecision(6, 2);
        entity.Property(x => x.HealthConditionLabel).HasMaxLength(200);
        entity.Property(x => x.HealthNotes).HasMaxLength(2000);
    }

    private static void ConfigureCategories(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Category>();
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasField("_name").HasMaxLength(120).IsRequired();
        entity.Property(x => x.OwnerUserId);
        entity.HasIndex(x => x.OwnerUserId);
        entity.HasIndex(x => x.Name)
            .IsUnique()
            .HasFilter("OwnerUserId IS NULL");
        entity.HasIndex(x => new { x.OwnerUserId, x.Name })
            .IsUnique()
            .HasFilter("OwnerUserId IS NOT NULL");
        entity.HasOne(x => x.OwnerUser)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Property(x => x.Description).HasMaxLength(500);
    }

    private static void ConfigureProducts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Product>();
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasField("_name").HasMaxLength(160).IsRequired();
        entity.HasIndex(x => x.Name);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.Basis).HasConversion<string>().HasMaxLength(40).IsRequired();
        entity.Property(x => x.ReferenceAmount).HasPrecision(12, 3).IsRequired();
        entity.Property(x => x.Calories).HasPrecision(12, 3).IsRequired();
        entity.Property(x => x.ProteinG).HasPrecision(12, 3).IsRequired();
        entity.Property(x => x.FatG).HasPrecision(12, 3).IsRequired();
        entity.Property(x => x.CarbsG).HasPrecision(12, 3).IsRequired();
        entity.Property(x => x.AllergensJson).IsRequired();
        entity.Property(x => x.DietaryTagsJson).IsRequired();
        entity.Property(x => x.IsGlobal).IsRequired();
        entity.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDishes(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Dish>();
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasField("_name").HasMaxLength(160).IsRequired();
        entity.HasIndex(x => x.Name);
        entity.Property(x => x.Description).HasMaxLength(1000);
        entity.Property(x => x.OwnerUserId);
        entity.HasIndex(x => x.OwnerUserId);
        entity.HasOne(x => x.OwnerUser)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Category).WithMany(x => x.Dishes).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);

        var ingredient = modelBuilder.Entity<DishIngredient>();
        ingredient.HasKey(x => x.Id);
        ingredient.Property(x => x.Amount).HasPrecision(12, 3).IsRequired();
        ingredient.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        ingredient.HasIndex(x => new { x.DishId, x.ProductId }).IsUnique();
        ingredient.HasOne(x => x.Dish).WithMany(x => x.Ingredients).HasForeignKey(x => x.DishId).OnDelete(DeleteBehavior.Cascade);
        ingredient.HasOne(x => x.Product).WithMany(x => x.DishIngredients).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureUserCatalogProducts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserCatalogProduct>();
        entity.HasKey(x => new { x.UserId, x.ProductId });
        entity.Property(x => x.AddedAtUtc).IsRequired();
        entity.HasOne(x => x.User)
            .WithMany(x => x.CatalogProducts)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(x => x.Product)
            .WithMany(x => x.UserCatalogItems)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureRestrictions(ModelBuilder modelBuilder)
    {
        var restriction = modelBuilder.Entity<DietaryRestriction>();
        restriction.HasKey(x => x.Id);
        restriction.Property(x => x.Name).HasMaxLength(200).IsRequired();
        restriction.HasIndex(x => x.Name).IsUnique();
        restriction.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        restriction.HasDiscriminator<string>("RestrictionType")
            .HasValue<AllergenRestriction>("Allergen")
            .HasValue<ForbiddenProductRestriction>("ForbiddenProduct")
            .HasValue<DietModeRestriction>("DietMode");

        modelBuilder.Entity<AllergenRestriction>().Property(x => x.AllergenName).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<ForbiddenProductRestriction>().Property(x => x.ProductId).IsRequired();
        modelBuilder.Entity<DietModeRestriction>().Property(x => x.ModeName).HasMaxLength(100).IsRequired();
        modelBuilder.Entity<DietModeRestriction>().Property(x => x.ExcludedTagsJson).IsRequired();

        var userRestriction = modelBuilder.Entity<UserRestriction>();
        userRestriction.HasKey(x => new { x.UserId, x.DietaryRestrictionId });
        userRestriction.HasOne(x => x.User).WithMany(x => x.Restrictions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        userRestriction.HasOne(x => x.DietaryRestriction).WithMany().HasForeignKey(x => x.DietaryRestrictionId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureMealIntakes(ModelBuilder modelBuilder)
    {
        var meal = modelBuilder.Entity<MealIntake>();
        meal.HasKey(x => x.Id);
        meal.Property(x => x.MealType).HasConversion<string>().HasMaxLength(30).IsRequired();
        meal.Property(x => x.Name).HasMaxLength(160).IsRequired();
        meal.HasOne(x => x.User).WithMany(x => x.MealIntakes).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var item = modelBuilder.Entity<MealIntakeItem>();
        item.HasKey(x => x.Id);
        item.Property(x => x.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        item.Property(x => x.ItemName).HasMaxLength(160).IsRequired();
        item.Property(x => x.Amount).HasPrecision(12, 3).IsRequired();
        item.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        item.Property(x => x.Calories).HasPrecision(12, 3).IsRequired();
        item.Property(x => x.ProteinG).HasPrecision(12, 3).IsRequired();
        item.Property(x => x.FatG).HasPrecision(12, 3).IsRequired();
        item.Property(x => x.CarbsG).HasPrecision(12, 3).IsRequired();
        item.HasOne(x => x.MealIntake).WithMany(x => x.Items).HasForeignKey(x => x.MealIntakeId).OnDelete(DeleteBehavior.Cascade);
        item.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        item.HasOne(x => x.Dish).WithMany().HasForeignKey(x => x.DishId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePlans(ModelBuilder modelBuilder)
    {
        var plan = modelBuilder.Entity<NutritionPlan>();
        plan.HasKey(x => x.Id);
        plan.Property(x => x.TargetCalories).HasPrecision(12, 3).IsRequired();
        plan.Property(x => x.TargetProteinG).HasPrecision(12, 3).IsRequired();
        plan.Property(x => x.TargetFatG).HasPrecision(12, 3).IsRequired();
        plan.Property(x => x.TargetCarbsG).HasPrecision(12, 3).IsRequired();
        plan.HasIndex(x => new { x.UserId, x.PlanDate });
        plan.HasOne(x => x.User).WithMany(x => x.Plans).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var item = modelBuilder.Entity<PlanItem>();
        item.HasKey(x => x.Id);
        item.Property(x => x.MealType).HasConversion<string>().HasMaxLength(30).IsRequired();
        item.Property(x => x.MealName).HasMaxLength(160).IsRequired();
        item.Property(x => x.PortionAmount).HasPrecision(12, 3).IsRequired();
        item.Property(x => x.PortionUnit).HasMaxLength(30).IsRequired();
        item.Property(x => x.Calories).HasPrecision(12, 3).IsRequired();
        item.Property(x => x.ProteinG).HasPrecision(12, 3).IsRequired();
        item.Property(x => x.FatG).HasPrecision(12, 3).IsRequired();
        item.Property(x => x.CarbsG).HasPrecision(12, 3).IsRequired();
        item.HasOne(x => x.NutritionPlan).WithMany(x => x.Items).HasForeignKey(x => x.NutritionPlanId).OnDelete(DeleteBehavior.Cascade);
        item.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        item.HasOne(x => x.Dish).WithMany().HasForeignKey(x => x.DishId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePlanHistory(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlanHistory>();
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SnapshotJson).IsRequired();
        entity.HasIndex(x => new { x.UserId, x.SavedAtUtc });
        entity.HasOne(x => x.User).WithMany(x => x.PlanHistory).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureActionRecords(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ActionRecord>();
        entity.HasKey(x => x.Id);
        entity.Property(x => x.ActionType).HasConversion<string>().HasMaxLength(40).IsRequired();
        entity.Property(x => x.EntityName).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.BeforeJson);
        entity.Property(x => x.AfterJson);
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        entity.HasOne(x => x.User).WithMany(x => x.ActionRecords).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
