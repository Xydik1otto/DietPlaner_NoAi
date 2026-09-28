using System.Windows;
using System.Windows.Controls;
using DietPlanner.ViewModels;

namespace DietPlanner.Views;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        Loaded += DashboardPage_Loaded;
    }

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel viewModel)
        {
            await viewModel.LoadDashboardDataAsync();
        }
    }
}