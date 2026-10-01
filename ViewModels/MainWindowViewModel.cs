using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Infrastructure;
using DietPlanner.Models;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly INavigationService _navigation;
    private readonly IAuthenticationService _auth;
    private readonly ILocalizationService _loc;
    private readonly IAppPaths _paths;

    [ObservableProperty] private Type? _currentViewModelType;
    
    // Глобальне оголошення для всіх
    [ObservableProperty] private string _broadcastMessage = string.Empty;
    [ObservableProperty] private bool _isBroadcastVisible;

    // Сповіщення про критичні помилки ТІЛЬКИ ДЛЯ АДМІНА
    [ObservableProperty] private string _adminCriticalErrorMessage = string.Empty;
    [ObservableProperty] private bool _isAdminCriticalErrorVisible;

    public bool IsDashboardActive => CurrentViewModelType == typeof(DashboardViewModel);
    public bool IsCatalogActive => CurrentViewModelType == typeof(CatalogViewModel);
    public bool IsPlanGeneratorActive => CurrentViewModelType == typeof(PlanGeneratorViewModel);
    public bool IsProfileActive => CurrentViewModelType == typeof(ProfileViewModel);
    public bool IsStatisticsActive => CurrentViewModelType == typeof(StatisticsViewModel);
    public bool IsUndoHistoryActive => CurrentViewModelType == typeof(UndoHistoryViewModel);
    public bool IsAdminActive => CurrentViewModelType == typeof(AdminViewModel);

    public MainWindowViewModel(
        SessionService session, 
        INavigationService navigation, 
        IAuthenticationService auth,
        ILocalizationService loc,
        IAppPaths paths)
    {
        _session = session;
        _navigation = navigation;
        _auth = auth;
        _loc = loc;
        _paths = paths;

        _navigation.NavigationRequested += OnNavigationRequested;
        _session.StateChanged += (s, e) => { RefreshState(); CheckAlerts(); };
        _loc.CultureChanged += (s, e) => OnPropertyChanged(nameof(CurrentLanguageName));

        CheckAlerts();
    }

    public void CheckAlerts()
    {
        CheckForBroadcast();
        CheckForAdminCriticalErrors();
    }

    public void CheckForBroadcast()
    {
        try
        {
            var broadcastPath = Path.Combine(_paths.LogsDirectory, "system_broadcast.txt");
            var dismissedPath = Path.Combine(_paths.LogsDirectory, "dismissed_broadcast.txt");

            if (File.Exists(broadcastPath))
            {
                var text = File.ReadAllText(broadcastPath, Encoding.UTF8).Trim();
                var dismissedText = File.Exists(dismissedPath) ? File.ReadAllText(dismissedPath, Encoding.UTF8).Trim() : string.Empty;

                if (!string.IsNullOrEmpty(text) && text != dismissedText)
                {
                    BroadcastMessage = text;
                    IsBroadcastVisible = true;
                    return;
                }
            }
        }
        catch { }

        IsBroadcastVisible = false;
    }

    // 🚨 СПОВІЩЕННЯ ПРО КРИТИЧНІ ПОМИЛКИ ДЛЯ АДМІНА
    public void CheckForAdminCriticalErrors()
    {
        if (!IsAdmin)
        {
            IsAdminCriticalErrorVisible = false;
            return;
        }

        try
        {
            var dismissedErrPath = Path.Combine(_paths.LogsDirectory, "dismissed_admin_error.txt");
            var dismissedErrText = File.Exists(dismissedErrPath) ? File.ReadAllText(dismissedErrPath, Encoding.UTF8).Trim() : string.Empty;

            if (Directory.Exists(_paths.LogsDirectory))
            {
                var logFiles = Directory.GetFiles(_paths.LogsDirectory, "*.log")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(5);

                foreach (var file in logFiles)
                {
                    var content = File.ReadAllText(file, Encoding.UTF8);
                    if (content.Contains("[ERROR]") || content.Contains("[CRITICAL]") || content.Contains("Exception"))
                    {
                        var msg = "🚨 Виявлено критичні помилки у системних логах!";
                        if (msg != dismissedErrText)
                        {
                            AdminCriticalErrorMessage = msg;
                            IsAdminCriticalErrorVisible = true;
                            return;
                        }
                    }
                }
            }
        }
        catch { }

        IsAdminCriticalErrorVisible = false;
    }

    [RelayCommand]
    private void DismissBroadcast()
    {
        try
        {
            Directory.CreateDirectory(_paths.LogsDirectory);
            var dismissedPath = Path.Combine(_paths.LogsDirectory, "dismissed_broadcast.txt");
            File.WriteAllText(dismissedPath, BroadcastMessage, Encoding.UTF8);
        }
        catch { }

        IsBroadcastVisible = false;
    }

    [RelayCommand]
    private void DismissAdminCriticalError()
    {
        try
        {
            Directory.CreateDirectory(_paths.LogsDirectory);
            var dismissedErrPath = Path.Combine(_paths.LogsDirectory, "dismissed_admin_error.txt");
            File.WriteAllText(dismissedErrPath, AdminCriticalErrorMessage, Encoding.UTF8);
        }
        catch { }

        IsAdminCriticalErrorVisible = false;
    }

    public string CurrentLanguageName => _loc.CurrentCulture.TwoLetterISOLanguageName.ToUpperInvariant();

    private void OnNavigationRequested(object? sender, object viewModel)
    {
        CurrentViewModelType = viewModel.GetType();
        CheckAlerts();
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

    [RelayCommand] private void SwitchLanguage(string cultureCode) => _loc.SetCulture(new CultureInfo(cultureCode));
    [RelayCommand] private void NavigateDashboard() => _navigation.Navigate<DashboardViewModel>();
    [RelayCommand] private void NavigateCatalog() => _navigation.Navigate<CatalogViewModel>();
    [RelayCommand] private void NavigatePlanGenerator() => _navigation.Navigate<PlanGeneratorViewModel>();
    [RelayCommand] private void NavigateProfile() => _navigation.Navigate<ProfileViewModel>();
    [RelayCommand] private void NavigateStatistics() => _navigation.Navigate<StatisticsViewModel>();
    [RelayCommand] private void NavigateUndoHistory() => _navigation.Navigate<UndoHistoryViewModel>();
    [RelayCommand] private void NavigateAdmin() => _navigation.Navigate<AdminViewModel>();

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _auth.LogoutAsync();
        _navigation.Navigate<AuthViewModel>();
    }
}