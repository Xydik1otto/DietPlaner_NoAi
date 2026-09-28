using System.Windows.Controls;
using DietPlanner.ViewModels;

namespace DietPlanner.Views;

public partial class PlanGeneratorPage : Page
{
    public PlanGeneratorPage(PlanGeneratorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (s, e) =>
        {
            if (DataContext is PlanGeneratorViewModel vm)
            {
                await vm.LoadHistoryAsync();
            }
        };
    }
}