using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Animus.Common;
using Animus.Models;
using Animus.Services;

namespace Animus.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly Action<UserAccount> _onAuthenticated;

    private string _login = "";
    private string _password = "";
    private string _status = "AGUARDANDO IDENTIFICAÇÃO";
    private bool _hasError;
    private bool _isBusy;

    public LoginViewModel(AuthService auth, Action<UserAccount> onAuthenticated)
    {
        _auth = auth;
        _onAuthenticated = onAuthenticated;
        SubmitCommand = new AsyncRelayCommand(SubmitAsync);
    }

    public ICommand SubmitCommand { get; }

    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Limpa os campos ao sair da sessao.</summary>
    public void Reset()
    {
        Login = "";
        Password = "";
        HasError = false;
        Status = "AGUARDANDO IDENTIFICAÇÃO";
    }

    private async Task SubmitAsync()
    {
        IsBusy = true;
        HasError = false;
        Status = "VERIFICANDO...";

        // Pequena pausa: evita resposta instantanea e da feedback visual.
        await Task.Delay(250);

        if (_auth.TryLogin(Login, Password, out var account, out var error) && account is not null)
        {
            Status = "ACESSO LIBERADO";
            IsBusy = false;
            Password = "";
            _onAuthenticated(account);
            return;
        }

        Password = "";
        HasError = true;
        Status = error;
        IsBusy = false;
    }
}
