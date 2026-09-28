using System.Windows;
using System.Windows.Controls;
using DietPlanner.ViewModels;

namespace DietPlanner.Views;

public partial class AuthPage : Page
{
    public AuthPage()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AuthViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.Password = passwordBox.Password;
        }
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AuthViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.PasswordConfirmation = passwordBox.Password;
        }
    }
}