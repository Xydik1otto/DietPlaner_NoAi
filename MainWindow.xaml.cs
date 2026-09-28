using System.Windows;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.ViewModels;

namespace DietPlanner;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel, INavigationService navigationService)
    {
        InitializeComponent();
        DataContext = viewModel;

        if (navigationService is NavigationService nav)
            nav.RegisterFrame(MainFrame);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}