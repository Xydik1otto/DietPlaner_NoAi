using System.Text.Json;

namespace DietPlanner.Models;

public sealed class PlanHistory
{
    private PlanHistory()
    {
    }

    public PlanHistory(Guid userId, Guid planId, NutritionPlan plan)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        NutritionPlanId = planId;
        SavedAtUtc = DateTime.UtcNow;
        SnapshotJson = JsonSerializer.Serialize(new PlanSnapshot(
            plan.PlanDate,
            plan.MealCount,
            plan.TargetCalories,
            plan.TargetProteinG,
            plan.TargetFatG,
            plan.TargetCarbsG,
            plan.Items.Select(i => new PlanSnapshotItem(
                i.MealType,
                i.MealName,
                i.ProductId,
                i.DishId,
                i.PortionAmount,
                i.PortionUnit,
                i.Calories,
                i.ProteinG,
                i.FatG,
                i.CarbsG)).ToArray()));
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public Guid NutritionPlanId { get; private set; }
    public DateTime SavedAtUtc { get; private set; }
    public string SnapshotJson { get; private set; } = string.Empty;
}

public sealed record PlanSnapshot(
    DateTime PlanDate,
    int MealCount,
    decimal TargetCalories,
    decimal TargetProteinG,
    decimal TargetFatG,
    decimal TargetCarbsG,
    IReadOnlyList<PlanSnapshotItem> Items);

public sealed record PlanSnapshotItem(
    MealType MealType,
    string MealName,
    Guid? ProductId,
    Guid? DishId,
    decimal PortionAmount,
    string PortionUnit,
    decimal Calories,
    decimal ProteinG,
    decimal FatG,
    decimal CarbsG);
