namespace DietPlanner.Services.Contracts;

public interface INavigationService
{
    event EventHandler<object>? NavigationRequested;
    void Navigate(object viewModel);
    void Navigate<TViewModel>() where TViewModel : notnull;
}
