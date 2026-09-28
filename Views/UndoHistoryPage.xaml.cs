using System.Windows.Controls;
using DietPlanner.ViewModels;

namespace DietPlanner.Views;

public partial class UndoHistoryPage : Page
{
    public UndoHistoryPage(UndoHistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (s, e) =>
        {
            if (DataContext is UndoHistoryViewModel vm)
            {
                await vm.LoadHistoryAsync();
            }
        };
    }
}