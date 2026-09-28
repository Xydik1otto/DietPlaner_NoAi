using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DietPlanner.Services.Contracts;
using DietPlanner.ViewModels;
using DietPlanner.Views;

namespace DietPlanner.Services;

public class NavigationService : INavigationService
{
    private Frame? _frame;

    public event EventHandler<object>? NavigationRequested;

    public void RegisterFrame(Frame frame)
    {
        _frame = frame;
    }

    public void Navigate(object viewModel)
    {
        NavigationRequested?.Invoke(this, viewModel);

        if (_frame == null) return;

        Page? page = viewModel switch
        {
            AuthViewModel => App.Services.GetRequiredService<AuthPage>(),
            DashboardViewModel => App.Services.GetRequiredService<DashboardPage>(),
            ProfileViewModel => App.Services.GetRequiredService<ProfilePage>(),
            CatalogViewModel => App.Services.GetRequiredService<CatalogPage>(),
            PlanGeneratorViewModel => App.Services.GetRequiredService<PlanGeneratorPage>(),
            StatisticsViewModel => App.Services.GetRequiredService<StatisticsPage>(),
            UndoHistoryViewModel => App.Services.GetRequiredService<UndoHistoryPage>(),
            AdminViewModel => App.Services.GetRequiredService<AdminPage>(),
            _ => null
        };

        if (page != null)
        {
            page.DataContext = viewModel;
            _frame.Navigate(page);

            // Очищаємо історію навігації, щоб унеможливити накладання сторінок
            while (_frame.CanGoBack)
            {
                _frame.RemoveBackEntry();
            }
        }
    }

    public void Navigate<TViewModel>() where TViewModel : notnull
    {
        var viewModel = App.Services.GetRequiredService<TViewModel>();
        Navigate(viewModel);
    }
}