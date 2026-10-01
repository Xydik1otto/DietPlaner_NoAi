using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Infrastructure;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public sealed partial class UserDisplayDto : ObservableObject
{
    public Guid Id { get; set; }
    public string ShortId => Id.ToString()[..8].ToUpperInvariant();
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string RoleDisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed partial class AdminViewModel : ViewModelBase
{
    private readonly IUserService _userService;
    private readonly IAuthorizationService _authService;
    private readonly INavigationService _navigationService;
    private readonly IAppPaths _paths;
    private readonly ILocalizationService _loc;
    private readonly IStatisticsService _statisticsService;

    [ObservableProperty] private ObservableCollection<UserDisplayDto> _users = new();
    [ObservableProperty] private string _criticalLogsContent = string.Empty;
    [ObservableProperty] private string _userLogsContent = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasAdminAccess = true;

    [ObservableProperty] private int _totalUsersCount;
    [ObservableProperty] private int _activeUsersCount;
    [ObservableProperty] private int _totalPlansCount;

    [ObservableProperty] private string _broadcastText = string.Empty;
    [ObservableProperty] private string _activeBroadcastStatus = "⚪ Поточне сповіщення відсутнє";

    public AdminViewModel(
        IUserService userService,
        IAuthorizationService authService,
        INavigationService navigationService,
        IAppPaths paths,
        ILocalizationService loc,
        IStatisticsService statisticsService)
    {
        _userService = userService;
        _authService = authService;
        _navigationService = navigationService;
        _paths = paths;
        _loc = loc;
        _statisticsService = statisticsService;
    }

    public async Task InitializeAsync()
    {
        HasAdminAccess = _authService.CanViewAdminArea;
        if (!HasAdminAccess)
        {
            StatusMessage = _loc.GetString("Admin_AccessDenied");
            return;
        }

        LoadCurrentBroadcast();
        await LoadUsersAndStatsAsync();
        await LoadLogsAsync();
    }

    // ⚡ Автоматичний виклик при перемиканні вкладок у UI
    public async Task OnTabChangedAsync(int tabIndex)
    {
        if (!HasAdminAccess) return;

        switch (tabIndex)
        {
            case 0: // Статистика
            case 1: // Користувачі
                await LoadUsersAndStatsAsync();
                break;
            case 2: // Логи
                await LoadLogsAsync();
                break;
            case 3: // Глобальне сповіщення
                LoadCurrentBroadcast();
                break;
        }
    }

    private void LoadCurrentBroadcast()
    {
        try
        {
            var broadcastPath = Path.Combine(_paths.LogsDirectory, "system_broadcast.txt");
            if (File.Exists(broadcastPath))
            {
                var text = File.ReadAllText(broadcastPath, Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    BroadcastText = text;
                    ActiveBroadcastStatus = $"🟢 Активне сповіщення: \"{text}\"";
                    return;
                }
            }
        }
        catch { }

        ActiveBroadcastStatus = "⚪ Поточне сповіщення відсутнє";
    }

    private string GetLocalizedRoleName(UserRole role)
    {
        var key = role == UserRole.Admin ? "Role_Admin" : "Role_User";
        var localized = _loc.GetString(key);

        if (string.IsNullOrEmpty(localized) || localized.StartsWith("["))
        {
            return role == UserRole.Admin ? "Адміністратор" : "Користувач";
        }
        return localized;
    }

    [RelayCommand]
    private async Task LoadUsersAndStatsAsync()
    {
        try
        {
            var userList = await _userService.GetAllAsync();
            var usersArray = userList.ToList();

            Users = new ObservableCollection<UserDisplayDto>(
                usersArray.Select(u => new UserDisplayDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    DisplayName = string.IsNullOrWhiteSpace(u.DisplayName) ? u.Email : u.DisplayName,
                    Role = u.Role,
                    RoleDisplayName = GetLocalizedRoleName(u.Role),
                    CreatedAt = u.CreatedAtUtc.ToLocalTime()
                }));

            var adminStats = await _statisticsService.GetAdminStatisticsAsync();
            TotalUsersCount = adminStats.TotalUsers;
            ActiveUsersCount = adminStats.UsersWithLogin;
            TotalPlansCount = adminStats.TotalPlans;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ToggleRoleAsync(UserDisplayDto? userDto)
    {
        if (userDto == null) return;

        var newRole = userDto.Role == UserRole.Admin ? UserRole.User : UserRole.Admin;
        try
        {
            await _userService.ChangeRoleAsync(userDto.Id, newRole);
            await LoadUsersAndStatsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteUserWithoutWarningAsync(UserDisplayDto? userDto)
    {
        if (userDto == null) return;

        try
        {
            await _userService.DeleteAsync(userDto.Id);
            await LoadUsersAndStatsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        try
        {
            const string noCritStr = "✅ Критичних помилок не виявлено.";
            const string noLogsStr = "ℹ️ Логи дій користувачів відсутні.";

            if (!Directory.Exists(_paths.LogsDirectory))
            {
                CriticalLogsContent = noCritStr;
                UserLogsContent = noLogsStr;
                return;
            }

            var logFiles = Directory.GetFiles(_paths.LogsDirectory, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(10)
                .ToList();

            if (logFiles.Count == 0)
            {
                CriticalLogsContent = noCritStr;
                UserLogsContent = noLogsStr;
                return;
            }

            var criticalSb = new StringBuilder();
            var userSb = new StringBuilder();

            foreach (var file in logFiles)
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = await reader.ReadToEndAsync();

                var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.Contains("[ERROR]") || line.Contains("[CRITICAL]") || line.Contains("Exception"))
                        criticalSb.AppendLine(line);
                    else
                        userSb.AppendLine(line);
                }
            }

            CriticalLogsContent = criticalSb.Length > 0 ? criticalSb.ToString() : noCritStr;
            UserLogsContent = userSb.Length > 0 ? userSb.ToString() : noLogsStr;
        }
        catch (Exception ex)
        {
            CriticalLogsContent = ex.Message;
            UserLogsContent = ex.Message;
        }
    }

    [RelayCommand]
    private void SendBroadcast()
    {
        if (string.IsNullOrWhiteSpace(BroadcastText)) return;

        Directory.CreateDirectory(_paths.LogsDirectory);
        var broadcastPath = Path.Combine(_paths.LogsDirectory, "system_broadcast.txt");
        
        var dismissedPath = Path.Combine(_paths.LogsDirectory, "dismissed_broadcast.txt");
        if (File.Exists(dismissedPath)) File.Delete(dismissedPath);

        File.WriteAllText(broadcastPath, BroadcastText, Encoding.UTF8);

        ActiveBroadcastStatus = $"🟢 Активне сповіщення: \"{BroadcastText}\"";
        StatusMessage = "📢 Оголошення успішно опубліковано!";
    }

    [RelayCommand]
    private void ClearBroadcast()
    {
        BroadcastText = string.Empty;
        var broadcastPath = Path.Combine(_paths.LogsDirectory, "system_broadcast.txt");
        var dismissedPath = Path.Combine(_paths.LogsDirectory, "dismissed_broadcast.txt");

        if (File.Exists(broadcastPath)) File.Delete(broadcastPath);
        if (File.Exists(dismissedPath)) File.Delete(dismissedPath);

        ActiveBroadcastStatus = "⚪ Поточне сповіщення відсутнє";
        StatusMessage = "🗑️ Оголошення видалено";
    }

    [RelayCommand]
    private void Back() => _navigationService.Navigate<DashboardViewModel>();
}