using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public sealed partial class AuthViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authentication;
    private readonly IValidationService _validation;
    private readonly INavigationService _navigation;
    private readonly ILoggingService _logging;

    [ObservableProperty]
    private bool _isRegisterMode;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _passwordConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty]
    private bool _isBusy;

    public AuthViewModel(
        IAuthenticationService authentication,
        IValidationService validation,
        INavigationService navigation,
        ILoggingService logging)
    {
        _authentication = authentication;
        _validation = validation;
        _navigation = navigation;
        _logging = logging;
    }

    public string ModeTitle => IsRegisterMode ? "Створення облікового запису" : "Вхід";
    public string SubmitTitle => IsRegisterMode ? "Зареєструватися" : "Увійти";
    public string SwitchTitle => IsRegisterMode ? "Уже є обліковий запис? Увійти" : "Немає облікового запису? Реєстрація";

    partial void OnIsRegisterModeChanged(bool value)
    {
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(ModeTitle));
        OnPropertyChanged(nameof(SubmitTitle));
        OnPropertyChanged(nameof(SwitchTitle));
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsRegisterMode = !IsRegisterMode;
        Password = string.Empty;
        PasswordConfirmation = string.Empty;
    }
    
    

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = string.Empty;

        try
        {
            var errors = new List<string>();

            if (IsRegisterMode)
            {
                errors.AddRange(_validation.ValidateEmail(Email));
                errors.AddRange(_validation.ValidatePassword(Password));
                errors.AddRange(_validation.ValidateRequiredText(DisplayName, "Ім'я", 120));
                if (Password != PasswordConfirmation)
                {
                    errors.Add("Підтвердження пароля не збігається.");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Email))
                    errors.Add("Введіть Email.");
                if (string.IsNullOrWhiteSpace(Password))
                    errors.Add("Введіть пароль.");
            }

            if (errors.Count > 0)
            {
                StatusMessage = string.Join(Environment.NewLine, errors);
                return;
            }

            var result = IsRegisterMode
                ? await _authentication.RegisterAsync(Email, DisplayName, Password, PasswordConfirmation)
                : await _authentication.LoginAsync(Email, Password);

            StatusMessage = result.Message;

            if (result.Success)
            {
                if (App.Services.GetService(typeof(MainWindowViewModel)) is MainWindowViewModel mainVm)
                {
                    mainVm.UpdateAuthenticationState();
                }

                _navigation.Navigate<DashboardViewModel>();
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"Помилка БД/Служби: {exception.InnerException?.Message ?? exception.Message}";
            await _logging.LogErrorAsync(_authentication.CurrentUser?.Id, exception, "Authentication operation failed");
        }
        finally
        {
            IsBusy = false;
        }
    }
}