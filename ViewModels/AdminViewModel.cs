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

public sealed partial class AdminViewModel : ViewModelBase
{
    private readonly IUserService _userService;
    private readonly IAuthorizationService _authService;
    private readonly INavigationService _navigationService;
    private readonly IAppPaths _paths;

    [ObservableProperty] private ObservableCollection<UserDisplayDto> _users = new();
    [ObservableProperty] private UserDisplayDto? _selectedUser;
    [ObservableProperty] private string _logsContent = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _hasAdminAccess;

    public AdminViewModel(
        IUserService userService,
        IAuthorizationService authService,
        INavigationService navigationService,
        IAppPaths paths)
    {
        _userService = userService;
        _authService = authService;
        _navigationService = navigationService;
        _paths = paths;

        HasAdminAccess = _authService.CanViewAdminArea;
    }

    public async Task InitializeAsync()
    {
        if (!HasAdminAccess)
        {
            StatusMessage = "Доступ обмежено. Потрібні права адміністратора.";
            return;
        }

        await LoadUsersAsync();
        await LoadLogsAsync();
    }

    [RelayCommand]
    private async Task LoadUsersAsync()
    {
        try
        {
            var userList = await _userService.GetAllAsync();
            Users = new ObservableCollection<UserDisplayDto>(
                userList.Select(u => new UserDisplayDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    DisplayName = u.DisplayName,
                    Role = u.Role,
                    CreatedAt = u.CreatedAtUtc.ToLocalTime()
                }));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка завантаження користувачів: {ex.Message}";
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
            StatusMessage = $"Роль користувача {userDto.DisplayName} змінено на {newRole}.";
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка зміни ролі: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteUserAsync(UserDisplayDto? userDto)
    {
        if (userDto == null) return;

        try
        {
            await _userService.DeleteAsync(userDto.Id);
            StatusMessage = $"Користувача {userDto.DisplayName} успішно видалено.";
            await LoadUsersAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка видалення користувача: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        try
        {
            if (!Directory.Exists(_paths.LogsDirectory))
            {
                LogsContent = "Директорія логів порожня.";
                return;
            }

            var logFiles = Directory.GetFiles(_paths.LogsDirectory, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(5)
                .ToList();

            if (logFiles.Count == 0)
            {
                LogsContent = "Лог-файли відсутні.";
                return;
            }

            var sb = new StringBuilder();
            foreach (var file in logFiles)
            {
                sb.AppendLine($"=== ФАЙЛ: {Path.GetFileName(file)} ===");
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = await reader.ReadToEndAsync();
                sb.AppendLine(content);
                sb.AppendLine();
            }

            LogsContent = sb.ToString();
        }
        catch (Exception ex)
        {
            LogsContent = $"Помилка зчитування логів: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Back() => _navigationService.Navigate<DashboardViewModel>();
}

public class UserDisplayDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
}