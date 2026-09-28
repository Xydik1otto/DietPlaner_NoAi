using System.Windows.Controls;
using DietPlanner.ViewModels;

namespace DietPlanner.Views;

public partial class AdminPage : Page
{
    public AdminPage(AdminViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Автоматична ініціалізація при відкритті сторінки
        Loaded += async (s, e) => await viewModel.InitializeAsync();
    }

    private async void MainAdminTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Перевіряємо, що подія викликана саме головним TabControl, а не внутрішніми елементами
        if (e.Source is TabControl tabControl && tabControl.Name == "MainAdminTabControl" && DataContext is AdminViewModel vm)
        {
            await vm.OnTabChangedAsync(tabControl.SelectedIndex);
        }
    }
}