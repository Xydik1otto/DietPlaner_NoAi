using System.Windows;
using System.Windows.Controls;
using DietPlanner.ViewModels;

namespace DietPlanner.Views;

public partial class CatalogPage : Page
{
    public CatalogPage()
    {
        InitializeComponent();
        Loaded += CatalogPage_Loaded;
    }

    private async void CatalogPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CatalogViewModel viewModel)
        {
            await viewModel.LoadDataAsync();
        }
    }

    private void ResetCategory_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CatalogViewModel vm)
        {
            vm.SelectedCategory = null;
            vm.SearchQuery = string.Empty;
        }
    }
}