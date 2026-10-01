using CommunityToolkit.Mvvm.ComponentModel;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public partial class MealSlotGroupDto : ObservableObject
{
    public string SlotName { get; set; } = string.Empty;
    public string CombinedItemNames { get; set; } = string.Empty;
    public decimal TotalPortionGrams { get; set; }
    public decimal TotalCalories { get; set; }
    public decimal TotalProteins { get; set; }
    public decimal TotalFats { get; set; }
    public decimal TotalCarbs { get; set; }

    public int ComponentCount { get; set; }
    public bool IsMultiComponent => ComponentCount > 1;
    public string MultiComponentNote { get; set; } = string.Empty;

    /// <summary>
    /// Формує локалізоване повідомлення про багатокомпонентний прийом їжі
    /// </summary>
    public void SetLocalization(ILocalizationService loc)
    {
        if (IsMultiComponent && loc != null)
        {
            var format = loc.GetString("Plan_MultiComponentNoteFormat");
            MultiComponentNote = string.Format(format, ComponentCount);
        }
        else
        {
            MultiComponentNote = string.Empty;
        }
    }
}