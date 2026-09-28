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
    private readonly ILocalizationService _loc;

    [ObservableProperty] private bool _isRegisterMode;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _passwordConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty] private bool _isBusy;

    public AuthViewModel(
        IAuthenticationService authentication,
        IValidationService validation,
        INavigationService navigation,
        ILoggingService logging,
        ILocalizationService loc)
    {
        _authentication = authentication;
        _validation = validation;
        _navigation = navigation;
        _logging = logging;
        _loc = loc;

        _loc.CultureChanged += (_, _) => RefreshTitles();
    }

    public string ModeTitle => IsRegisterMode 
        ? _loc.GetString("Auth_RegisterModeTitle") 
        : _loc.GetString("Auth_LoginModeTitle");

    public string SubmitTitle => IsRegisterMode 
        ? _loc.GetString("Auth_RegisterSubmit") 
        : _loc.GetString("Auth_LoginSubmit");

    public string SwitchTitle => IsRegisterMode 
        ? _loc.GetString("Auth_SwitchToLogin") 
        : _loc.GetString("Auth_SwitchToRegister");

    private void RefreshTitles()
    {
        OnPropertyChanged(nameof(ModeTitle));
        OnPropertyChanged(nameof(SubmitTitle));
        OnPropertyChanged(nameof(SwitchTitle));
    }

    partial void OnIsRegisterModeChanged(bool value)
    {
        StatusMessage = string.Empty;
        RefreshTitles();
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
                errors.AddRange(_validation.ValidateRequiredText(DisplayName, _loc.GetString("Auth_NameField"), 120));
                
                if (Password != PasswordConfirmation)
                {
                    errors.Add(_loc.GetString("Auth_PasswordMismatch"));
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Email))
                    errors.Add(_loc.GetString("Auth_EmailRequired"));
                if (string.IsNullOrWhiteSpace(Password))
                    errors.Add(_loc.GetString("Auth_PasswordRequired"));
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
            StatusMessage = exception.InnerException?.Message ?? exception.Message;
            await _logging.LogErrorAsync(_authentication.CurrentUser?.Id, exception, "Authentication failed");
        }
        finally
        {
            IsBusy = false;
        }
    }
}