using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public sealed class LanguageOption
{
    public CultureInfo Culture { get; init; } = new("uk-UA");
    public string NativeName { get; init; } = string.Empty;
}

public sealed class EnumOption<T>
{
    public T Value { get; init; } = default!;
    public string DisplayName { get; init; } = string.Empty;
}

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
    private readonly ILocalizationService _localization;

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private DateTime? _birthDate = DateTime.Today.AddYears(-25);
    [ObservableProperty] private string _heightCm = string.Empty;
    [ObservableProperty] private string _weightKg = string.Empty;
    
    [ObservableProperty] private EnumOption<Sex>? _selectedSex;
    [ObservableProperty] private EnumOption<ActivityLevel>? _selectedActivityLevel;
    [ObservableProperty] private EnumOption<NutritionGoal>? _selectedGoal;

    [ObservableProperty] private string _healthConditionLabel = string.Empty;
    [ObservableProperty] private string _healthNotes = string.Empty;

    [ObservableProperty] private string _newEmail = string.Empty;
    [ObservableProperty] private string _newPassword = string.Empty;
    [ObservableProperty] private string _verificationCodeInput = string.Empty;
    [ObservableProperty] private bool _isVerificationPending;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [ObservableProperty] private ObservableCollection<RestrictionItemViewModel> _restrictions = new();

    [ObservableProperty] private ObservableCollection<EnumOption<Sex>> _sexes = new();
    [ObservableProperty] private ObservableCollection<EnumOption<ActivityLevel>> _activityLevels = new();
    [ObservableProperty] private ObservableCollection<EnumOption<NutritionGoal>> _goals = new();

    [ObservableProperty] private List<LanguageOption> _availableLanguages = new();
    
    private LanguageOption? _selectedLanguage;
    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value) && value != null)
            {
                _localization.SetCulture(value.Culture);
                RefreshLocalizedOptions();
                StatusMessage = string.Empty;
            }
        }
    }

    private string? _generatedCode;
    private string? _pendingEmail;
    private string? _pendingPassword;

    public ProfileViewModel(
        IEmailService emailService,
        IAuthenticationService authentication,
        INavigationService navigation,
        IUserService users,
        IValidationService validation,
        IRestrictionService restrictionService,
        ILocalizationService localization)
    {
        _emailService = emailService;
        _authentication = authentication;
        _navigation = navigation;
        _users = users;
        _validation = validation;
        _restrictionService = restrictionService;
        _localization = localization;

        InitializeLanguages();
        RefreshLocalizedOptions();
        _ = LoadAsync();
    }

    public string Email => _authentication.CurrentUser?.Email ?? string.Empty;

    private void InitializeLanguages()
    {
        AvailableLanguages = new List<LanguageOption>
        {
            new LanguageOption { Culture = new CultureInfo("uk-UA"), NativeName = "🇺🇦 Українська" },
            new LanguageOption { Culture = new CultureInfo("en-US"), NativeName = "🇬🇧 English" }
        };

        var currentCulture = _localization.CurrentCulture;
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Culture.Name.Equals(currentCulture.Name, StringComparison.OrdinalIgnoreCase)) 
                           ?? AvailableLanguages.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    private void RefreshLocalizedOptions()
    {
        var currentSex = SelectedSex?.Value;
        var currentActivity = SelectedActivityLevel?.Value;
        var currentGoal = SelectedGoal?.Value;

        // 1. Стать
        Sexes = new ObservableCollection<EnumOption<Sex>>
        {
            new EnumOption<Sex> { Value = Sex.Male, DisplayName = _localization.GetString("Sex_Male") },
            new EnumOption<Sex> { Value = Sex.Female, DisplayName = _localization.GetString("Sex_Female") }
        };
        SelectedSex = Sexes.FirstOrDefault(s => s.Value == currentSex) ?? Sexes.FirstOrDefault();

        // 2. Рівень активності
        ActivityLevels = new ObservableCollection<EnumOption<ActivityLevel>>
        {
            new EnumOption<ActivityLevel> { Value = ActivityLevel.Sedentary, DisplayName = _localization.GetString("Activity_Sedentary") },
            new EnumOption<ActivityLevel> { Value = ActivityLevel.Light, DisplayName = _localization.GetString("Activity_Light") },
            new EnumOption<ActivityLevel> { Value = ActivityLevel.Moderate, DisplayName = _localization.GetString("Activity_Moderate") },
            new EnumOption<ActivityLevel> { Value = ActivityLevel.High, DisplayName = _localization.GetString("Activity_High") },
            new EnumOption<ActivityLevel> { Value = ActivityLevel.VeryHigh, DisplayName = _localization.GetString("Activity_VeryHigh") }
        };
        SelectedActivityLevel = ActivityLevels.FirstOrDefault(a => a.Value == currentActivity) ?? ActivityLevels.FirstOrDefault();

        // 3. Ціль харчування
        Goals = new ObservableCollection<EnumOption<NutritionGoal>>
        {
            new EnumOption<NutritionGoal> { Value = NutritionGoal.LoseWeight, DisplayName = _localization.GetString("Goal_LoseWeight") },
            new EnumOption<NutritionGoal> { Value = NutritionGoal.GainWeight, DisplayName = _localization.GetString("Goal_GainWeight") },
            new EnumOption<NutritionGoal> { Value = NutritionGoal.MaintainWeight, DisplayName = _localization.GetString("Goal_MaintainWeight") },
            new EnumOption<NutritionGoal> { Value = NutritionGoal.Recompose, DisplayName = _localization.GetString("Goal_Recompose") }
        };
        SelectedGoal = Goals.FirstOrDefault(g => g.Value == currentGoal) ?? Goals.FirstOrDefault();

        // 4. Оновлення мови для списку дієтичних обмежень та алергенів
        _ = ReloadRestrictionsAsync();
    }

    private async Task ReloadRestrictionsAsync()
    {
        var user = _authentication.CurrentUser;
        if (user is null) return;

        try
        {
            var available = await _restrictionService.GetAvailableAsync();
            var userRestrictions = await _restrictionService.GetForUserAsync(user.Id);
            var userRestrictionIds = userRestrictions.Select(r => r.Id).ToHashSet();

            var currentSelections = Restrictions.ToDictionary(r => r.Id, r => r.IsSelected);

            Restrictions.Clear();
            foreach (var r in available)
            {
                // Очищаємо пробіли та дефіси для формування нормалізованого ключа (напр. "Gluten-Free" -> "Restr_GlutenFree")
                var cleanName = r.Name.Replace(" ", "").Replace("-", "");
                var locNameKey = $"Restr_{cleanName}";
                var locDescKey = $"Restr_{cleanName}_Desc";

                var localizedName = _localization.GetString(locNameKey);
                if (string.IsNullOrEmpty(localizedName) || localizedName.StartsWith("["))
                    localizedName = r.Name;

                var localizedDesc = _localization.GetString(locDescKey);
                if (string.IsNullOrEmpty(localizedDesc) || localizedDesc.StartsWith("["))
                    localizedDesc = r.Description;

                bool isSelected = currentSelections.TryGetValue(r.Id, out var selected) 
                    ? selected 
                    : userRestrictionIds.Contains(r.Id);

                Restrictions.Add(new RestrictionItemViewModel
                {
                    Id = r.Id,
                    Name = localizedName,
                    Description = localizedDesc,
                    IsSelected = isSelected
                });
            }
        }
        catch
        {
            // Ігнорування помилки завантаження обмежень
        }
    }

    private async Task LoadAsync()
    {
        var user = _authentication.CurrentUser;
        if (user is null) return;

        DisplayName = user.DisplayName;
        BirthDate = user.BirthDate ?? DateTime.Today.AddYears(-25);
        HeightCm = user.HeightCm?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        WeightKg = user.WeightKg?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

        if (user.SexForCalculation.HasValue)
            SelectedSex = Sexes.FirstOrDefault(s => s.Value == user.SexForCalculation.Value);

        if (user.ActivityLevel.HasValue)
            SelectedActivityLevel = ActivityLevels.FirstOrDefault(a => a.Value == user.ActivityLevel.Value);

        if (user.Goal.HasValue)
            SelectedGoal = Goals.FirstOrDefault(g => g.Value == user.Goal.Value);

        HealthConditionLabel = user.HealthConditionLabel ?? string.Empty;
        HealthNotes = user.HealthNotes ?? string.Empty;

        await ReloadRestrictionsAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var user = _authentication.CurrentUser;
        if (user is null)
        {
            StatusMessage = _localization.GetString("Err_UserNotFound");
            return;
        }

        var nameLabel = _localization.GetString("Prof_DisplayName");
        var heightLabel = _localization.GetString("Prof_Height");
        var weightLabel = _localization.GetString("Prof_Weight");

        var errors = _validation.ValidateRequiredText(DisplayName, nameLabel, 120).ToList();
        errors.AddRange(_validation.ValidateDecimal(HeightCm, heightLabel, 50m, 250m));
        errors.AddRange(_validation.ValidateDecimal(WeightKg, weightLabel, 20m, 400m));
        
        if (BirthDate is null) errors.Add(string.Format(_localization.GetString("Val_RequiredField"), _localization.GetString("Prof_BirthDate")));
        if (SelectedSex is null) errors.Add(string.Format(_localization.GetString("Val_RequiredField"), _localization.GetString("Prof_Sex")));
        if (SelectedActivityLevel is null) errors.Add(string.Format(_localization.GetString("Val_RequiredField"), _localization.GetString("Prof_Activity")));
        if (SelectedGoal is null) errors.Add(string.Format(_localization.GetString("Val_RequiredField"), _localization.GetString("Prof_Goal")));

        if (errors.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, errors);
            return;
        }

        if (!decimal.TryParse(HeightCm.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var height) ||
            !decimal.TryParse(WeightKg.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var weight))
        {
            StatusMessage = _localization.GetString("Val_InvalidNumber");
            return;
        }

        user.SetDisplayName(DisplayName);
        user.UpdateNutritionProfile(
            BirthDate,
            SelectedSex?.Value,
            height,
            weight,
            SelectedActivityLevel?.Value,
            SelectedGoal?.Value,
            HealthConditionLabel,
            HealthNotes);

        await _users.UpdateProfileAsync(user);

        var selectedIds = Restrictions.Where(r => r.IsSelected).Select(r => r.Id);
        await _restrictionService.SetUserRestrictionsAsync(user.Id, selectedIds);

        StatusMessage = _localization.GetString("Prof_SaveProfile");
    }

    [RelayCommand]
    private async Task RequestSecurityChangeAsync()
    {
        var user = _authentication.CurrentUser;
        if (user == null) return;

        if (string.IsNullOrWhiteSpace(NewEmail) && string.IsNullOrWhiteSpace(NewPassword))
        {
            StatusMessage = _localization.GetString("Auth_FillRequiredFields");
            return;
        }

        _generatedCode = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        _pendingEmail = string.IsNullOrWhiteSpace(NewEmail) ? user.Email : NewEmail.Trim();
        _pendingPassword = string.IsNullOrWhiteSpace(NewPassword) ? null : NewPassword.Trim();

        try
        {
            var purposeText = _localization.GetString("Prof_Subtitle");
            await _emailService.SendVerificationCodeAsync(_pendingEmail, _generatedCode, purposeText);
            IsVerificationPending = true;
            StatusMessage = _localization.GetString("Prof_VerificationSent");
        }
        catch (Exception ex)
        {
            _generatedCode = null;
            _pendingEmail = null;
            _pendingPassword = null;
            IsVerificationPending = false;
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ConfirmSecurityChangeAsync()
    {
        if (VerificationCodeInput?.Trim() != _generatedCode)
        {
            StatusMessage = _localization.GetString("Auth_PasswordMismatchErr");
            return;
        }

        var user = _authentication.CurrentUser;
        if (user == null) return;

        if (!string.IsNullOrEmpty(_pendingEmail)) user.SetEmail(_pendingEmail);
        if (!string.IsNullOrEmpty(_pendingPassword)) user.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(_pendingPassword));

        await _users.UpdateProfileAsync(user);

        IsVerificationPending = false;
        _generatedCode = null;
        _pendingEmail = null;
        _pendingPassword = null;
        NewEmail = string.Empty;
        NewPassword = string.Empty;
        VerificationCodeInput = string.Empty;
        StatusMessage = _localization.GetString("Undo_SuccessMessage");
    }

    [RelayCommand]
    private void Back() => _navigation.Navigate<DashboardViewModel>();
}