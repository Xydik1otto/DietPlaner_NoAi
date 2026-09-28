using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public partial class RestrictionItemViewModel : ObservableObject
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class ProfileViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authentication;
    private readonly INavigationService _navigation;
    private readonly IUserService _users;
    private readonly IValidationService _validation;
    private readonly IEmailService _emailService;
    private readonly IRestrictionService _restrictionService;

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private DateTime? _birthDate = DateTime.Today.AddYears(-25);
    [ObservableProperty] private string _heightCm = string.Empty;
    [ObservableProperty] private string _weightKg = string.Empty;
    [ObservableProperty] private Sex? _sex;
    [ObservableProperty] private ActivityLevel? _activityLevel;
    [ObservableProperty] private NutritionGoal? _goal;
    [ObservableProperty] private string _healthConditionLabel = string.Empty;
    [ObservableProperty] private string _healthNotes = string.Empty;

    [ObservableProperty] private string _newEmail = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _verificationCodeInput = string.Empty;
    [ObservableProperty] private bool _isVerificationPending;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<RestrictionItemViewModel> _restrictions = new();

    private string? _generatedCode;
    private string? _pendingEmail;
    private string? _pendingPassword;

    public ProfileViewModel(
        IEmailService emailService,
        IAuthenticationService authentication,
        INavigationService navigation,
        IUserService users,
        IValidationService validation,
        IRestrictionService restrictionService)
    {
        _emailService = emailService;
        _authentication = authentication;
        _navigation = navigation;
        _users = users;
        _validation = validation;
        _restrictionService = restrictionService;

        _ = LoadAsync();
    }

    public Sex[] Sexes => Enum.GetValues<Sex>();
    public ActivityLevel[] ActivityLevels => Enum.GetValues<ActivityLevel>();
    public NutritionGoal[] NutritionGoals => Enum.GetValues<NutritionGoal>();

    public string Email => _authentication.CurrentUser?.Email ?? string.Empty;

    private async Task LoadAsync()
    {
        var user = _authentication.CurrentUser;
        if (user is null) return;

        DisplayName = user.DisplayName;
        BirthDate = user.BirthDate ?? DateTime.Today.AddYears(-25);
        HeightCm = user.HeightCm?.ToString("0.##") ?? string.Empty;
        WeightKg = user.WeightKg?.ToString("0.##") ?? string.Empty;
        Sex = user.SexForCalculation;
        ActivityLevel = user.ActivityLevel;
        Goal = user.Goal;
        HealthConditionLabel = user.HealthConditionLabel ?? string.Empty;
        HealthNotes = user.HealthNotes ?? string.Empty;

        try
        {
            var available = await _restrictionService.GetAvailableAsync();
            var userRestrictions = await _restrictionService.GetForUserAsync(user.Id);
            var userRestrictionIds = userRestrictions.Select(r => r.Id).ToHashSet();

            Restrictions.Clear();
            foreach (var r in available)
            {
                Restrictions.Add(new RestrictionItemViewModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    IsSelected = userRestrictionIds.Contains(r.Id)
                });
            }
        }
        catch
        {
            // Ошибка загрузки ограничений
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var user = _authentication.CurrentUser;
        if (user is null)
        {
            StatusMessage = "Немає активного користувача.";
            return;
        }

        var errors = _validation.ValidateRequiredText(DisplayName, "Ім'я", 120).ToList();
        errors.AddRange(_validation.ValidateDecimal(HeightCm, "Зріст (см)", 50m, 250m));
        errors.AddRange(_validation.ValidateDecimal(WeightKg, "Маса (кг)", 20m, 400m));
        if (BirthDate is null) errors.Add("Оберіть дату народження.");
        if (Sex is null) errors.Add("Стать для розрахунку не вибрана.");
        if (ActivityLevel is null) errors.Add("Рівень активності не вибраний.");
        if (Goal is null) errors.Add("Ціль не вибрана.");

        if (errors.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, errors);
            return;
        }

        if (!decimal.TryParse(HeightCm, out var height) || !decimal.TryParse(WeightKg, out var weight))
        {
            StatusMessage = "Некоректні числові значення.";
            return;
        }

        user.SetDisplayName(DisplayName);
        user.UpdateNutritionProfile(
            BirthDate,
            Sex,
            height,
            weight,
            ActivityLevel,
            Goal,
            HealthConditionLabel,
            HealthNotes);

        await _users.UpdateProfileAsync(user);

        // Сохранение выбранных аллергенов и режимов
        var selectedIds = Restrictions.Where(r => r.IsSelected).Select(r => r.Id);
        await _restrictionService.SetUserRestrictionsAsync(user.Id, selectedIds);

        StatusMessage = "Профіль та дієтичні обмеження успішно збережено!";
    }

    [RelayCommand]
    private async Task RequestSecurityChangeAsync()
    {
        var user = _authentication.CurrentUser;
        if (user == null) return;

        if (string.IsNullOrWhiteSpace(NewEmail) && string.IsNullOrWhiteSpace(NewPassword))
        {
            StatusMessage = "Введіть новий Email або новий пароль.";
            return;
        }

        _generatedCode = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        _pendingEmail = string.IsNullOrWhiteSpace(NewEmail) ? user.Email : NewEmail.Trim();
        _pendingPassword = string.IsNullOrWhiteSpace(NewPassword) ? null : NewPassword.Trim();

        try
        {
            StatusMessage = "Надсилаємо код підтвердження...";
            await _emailService.SendVerificationCodeAsync(_pendingEmail, _generatedCode, "зміни профілю");
            IsVerificationPending = true;
            StatusMessage = $"Код надіслано на {_pendingEmail}. Введіть 6-значний код нижче.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ConfirmSecurityChangeAsync()
    {
        if (VerificationCodeInput?.Trim() != _generatedCode)
        {
            StatusMessage = "Невірний код підтвердження!";
            return;
        }

        var user = _authentication.CurrentUser;
        if (user == null) return;

        if (!string.IsNullOrEmpty(_pendingEmail)) user.SetEmail(_pendingEmail);
        if (!string.IsNullOrEmpty(_pendingPassword)) user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(_pendingPassword));

        await _users.UpdateProfileAsync(user);

        IsVerificationPending = false;
        _generatedCode = null;
        NewEmail = string.Empty;
        NewPassword = string.Empty;
        VerificationCodeInput = string.Empty;
        StatusMessage = "Облікові дані успішно змінено!";
    }

    [RelayCommand]
    private void Back() => _navigation.Navigate<DashboardViewModel>();
}