using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly INavigationService _navigation;
    private readonly IAuthenticationService _auth;

    [ObservableProperty]
    private Type? _currentViewModelType;

    public bool IsDashboardActive => CurrentViewModelType == typeof(DashboardViewModel);
    public bool IsCatalogActive => CurrentViewModelType == typeof(CatalogViewModel);
    public bool IsPlanGeneratorActive => CurrentViewModelType == typeof(PlanGeneratorViewModel);
    public bool IsProfileActive => CurrentViewModelType == typeof(ProfileViewModel);
    public bool IsStatisticsActive => CurrentViewModelType == typeof(StatisticsViewModel);
    public bool IsUndoHistoryActive => CurrentViewModelType == typeof(UndoHistoryViewModel);
    public bool IsAdminActive => CurrentViewModelType == typeof(AdminViewModel);

    public MainWindowViewModel(SessionService session, INavigationService navigation, IAuthenticationService auth)
    {
        _session = session;
        _navigation = navigation;
        _auth = auth;

        _navigation.NavigationRequested += OnNavigationRequested;
        _session.StateChanged += (s, e) => RefreshState();
    }

    private void OnNavigationRequested(object? sender, object viewModel)
    {
        CurrentViewModelType = viewModel.GetType();
    }

    partial void OnCurrentViewModelTypeChanged(Type? value)
    {
        OnPropertyChanged(nameof(IsDashboardActive));
        OnPropertyChanged(nameof(IsCatalogActive));
        OnPropertyChanged(nameof(IsPlanGeneratorActive));
        OnPropertyChanged(nameof(IsProfileActive));
        OnPropertyChanged(nameof(IsStatisticsActive));
        OnPropertyChanged(nameof(IsUndoHistoryActive));
        OnPropertyChanged(nameof(IsAdminActive));
    }

    public bool IsAuthenticated => _session.IsAuthenticated;
    public bool IsAdmin => _session.CurrentUser?.Role == UserRole.Admin;
    public string UserDisplayName => _session.CurrentUser?.DisplayName ?? _session.CurrentUser?.Email ?? string.Empty;

    public void RefreshState()
    {
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(UserDisplayName));
    }

    public void UpdateAuthenticationState() => RefreshState();

    [RelayCommand]
    private void NavigateDashboard() => _navigation.Navigate<DashboardViewModel>();

    [RelayCommand]
    private void NavigateCatalog() => _navigation.Navigate<CatalogViewModel>();

    [RelayCommand]
    private void NavigatePlanGenerator() => _navigation.Navigate<PlanGeneratorViewModel>();

    [RelayCommand]
    private void NavigateProfile() => _navigation.Navigate<ProfileViewModel>();

    [RelayCommand]
    private void NavigateStatistics() => _navigation.Navigate<StatisticsViewModel>();

    [RelayCommand]
    private void NavigateUndoHistory() => _navigation.Navigate<UndoHistoryViewModel>();

    [RelayCommand]
    private void NavigateAdmin() => _navigation.Navigate<AdminViewModel>();

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _auth.LogoutAsync();
        _navigation.Navigate<AuthViewModel>();
    }
}