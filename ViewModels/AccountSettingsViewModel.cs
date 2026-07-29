using System.Windows.Input;
using Animus.Common;
using Animus.Models;
using Animus.Services;

namespace Animus.ViewModels;

/// <summary>Aba "Conta": troca de senha do usuario logado.</summary>
public sealed class AccountSettingsViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly NotificationService _notifications;
    private readonly UserAccount _user;

    private string _currentPassword = "";
    private string _newPassword = "";
    private string _confirmPassword = "";
    private string _passwordMessage = "";
    private bool _passwordSucceeded;

    public AccountSettingsViewModel(AuthService auth, NotificationService notifications, UserAccount user)
    {
        _auth = auth;
        _notifications = notifications;
        _user = user;
        ChangePasswordCommand = new RelayCommand(ChangePassword);
    }

    public string UserName => _user.DisplayName;

    public ICommand ChangePasswordCommand { get; }

    public string CurrentPassword
    {
        get => _currentPassword;
        set => SetProperty(ref _currentPassword, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set => SetProperty(ref _newPassword, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value);
    }

    public string PasswordMessage
    {
        get => _passwordMessage;
        private set
        {
            if (SetProperty(ref _passwordMessage, value))
                OnPropertyChanged(nameof(HasPasswordMessage));
        }
    }

    public bool HasPasswordMessage => !string.IsNullOrEmpty(PasswordMessage);

    /// <summary>true = mensagem de sucesso (verde), false = erro (vermelho).</summary>
    public bool PasswordSucceeded
    {
        get => _passwordSucceeded;
        private set => SetProperty(ref _passwordSucceeded, value);
    }

    private void ChangePassword()
    {
        if (_auth.TryChangePassword(_user.Id, CurrentPassword, NewPassword, ConfirmPassword, out var error))
        {
            CurrentPassword = NewPassword = ConfirmPassword = "";
            PasswordSucceeded = true;
            PasswordMessage = "Senha alterada com sucesso.";
            _notifications.SettingsSaved($"A senha de {UserName} foi alterada.");
            return;
        }

        PasswordSucceeded = false;
        PasswordMessage = error;
    }
}
