using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Animus.Common;
using Animus.Models;
using Animus.Services;
using Avalonia.Media.Imaging;

namespace Animus.ViewModels;

/// <summary>Controla a navegacao: login -> (dashboard | configuracoes) e o fundo do app.</summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly AppDataStore _store;
    private readonly AppearanceService _appearance;
    private readonly DoxService _dox;
    private readonly ClipboardService _clipboard;
    private readonly HotService _hot;
    private readonly FilePickerService _picker;

    private UserAccount? _user;
    private ViewModelBase? _currentPage;
    private Bitmap? _background;
    private bool _isSidebarExpanded = true;

    // As paginas sao criadas uma vez por sessao: navegar nao remonta a lista de fundos.
    private DashboardViewModel? _dashboard;
    private DoxViewModel? _doxPage;
    private HotViewModel? _hotPage;
    private SettingsViewModel? _settings;

    public MainWindowViewModel(
        AuthService auth,
        AppDataStore store,
        AppearanceService appearance,
        NotificationService notifications,
        DoxService dox,
        ClipboardService clipboard,
        HotService hot,
        FilePickerService picker)
    {
        _auth = auth;
        _store = store;
        _appearance = appearance;
        _dox = dox;
        _clipboard = clipboard;
        _hot = hot;
        _picker = picker;
        Notifications = notifications;

        // Ligar/desligar animacoes reflete na barra lateral (pulsos).
        _appearance.Changed += () => OnPropertyChanged(nameof(AnimationsEnabled));

        LoginPage = new LoginViewModel(auth, OnAuthenticated);

        ShowDashboardCommand = new RelayCommand(ShowDashboard);
        ShowDoxCommand = new RelayCommand(ShowDox);
        ShowHotCommand = new RelayCommand(ShowHot);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        LogoutCommand = new RelayCommand(Logout);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarExpanded = !IsSidebarExpanded);

        ApplyBackground(_store.Data.Background ?? BackgroundCatalog.List().FirstOrDefault());
    }

    public LoginViewModel LoginPage { get; }

    public NotificationService Notifications { get; }

    /// <summary>Liga o clipboard à janela (a área de transferência precisa do TopLevel).</summary>
    public void AttachClipboard(Avalonia.Controls.TopLevel topLevel) => _clipboard.Attach(topLevel);

    /// <summary>Liga o seletor de arquivos à janela (o 23 HOT precisa dele para os diálogos).</summary>
    public void AttachStorage(Avalonia.Controls.TopLevel topLevel) => _picker.Attach(topLevel);

    public ICommand ShowDashboardCommand { get; }
    public ICommand ShowDoxCommand { get; }
    public ICommand ShowHotCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand ToggleSidebarCommand { get; }

    /// <summary>Barra lateral recolhida deixa so os icones (a largura anima via transicao no XAML).</summary>
    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        private set
        {
            if (!SetProperty(ref _isSidebarExpanded, value)) return;
            OnPropertyChanged(nameof(SidebarWidth));
            OnPropertyChanged(nameof(SidebarLabelOpacity));
            OnPropertyChanged(nameof(SidebarLogoSize));
        }
    }

    public double SidebarWidth => IsSidebarExpanded ? 242 : 82;

    public double SidebarLabelOpacity => IsSidebarExpanded ? 1 : 0;

    public double SidebarLogoSize => IsSidebarExpanded ? 58 : 42;

    /// <summary>Liga/desliga os pulsos e brilhos (aba Aparência → Efeitos).</summary>
    public bool AnimationsEnabled => _appearance.Data.Animations;

    /// <summary>Conteudo da janela: a tela de login ou o app (o proprio VM, com template ShellView).</summary>
    public object RootContent => IsAuthenticated ? this : LoginPage;

    public ViewModelBase? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsDashboardActive));
            OnPropertyChanged(nameof(IsDoxActive));
            OnPropertyChanged(nameof(IsHotActive));
            OnPropertyChanged(nameof(IsSettingsActive));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }

    public Bitmap? Background
    {
        get => _background;
        private set => SetProperty(ref _background, value);
    }

    public bool IsAuthenticated => _user is not null;

    public string UserName => _user?.DisplayName ?? "";

    public string UserInitial => UserName.Length > 0 ? UserName[..1].ToUpperInvariant() : "?";

    public bool IsDashboardActive => CurrentPage is DashboardViewModel;

    public bool IsDoxActive => CurrentPage is DoxViewModel;

    public bool IsHotActive => CurrentPage is HotViewModel;

    public bool IsSettingsActive => CurrentPage is SettingsViewModel;

    public string PageTitle => CurrentPage switch
    {
        SettingsViewModel => "Configurações",
        DoxViewModel => "23 DOX",
        HotViewModel => "23 HOT",
        _ => "Dashboard",
    };

    public string PageSubtitle => CurrentPage switch
    {
        SettingsViewModel => "Conta e aparência do 23 Panel",
        DoxViewModel => "Consulta de dados",
        HotViewModel => "Verificador IMAP",
        _ => "Visão geral da sessão 23",
    };

    private void OnAuthenticated(UserAccount user)
    {
        _user = user;
        ShowDashboard();
        BackgroundCatalog.Prewarm();
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(UserInitial));
        OnPropertyChanged(nameof(RootContent));
    }

    private void ShowDashboard()
    {
        if (_user is null) return;
        CurrentPage = _dashboard ??= new DashboardViewModel(_user, _appearance);
    }

    private void ShowDox()
    {
        if (_user is null) return;
        CurrentPage = _doxPage ??= new DoxViewModel(_dox, _clipboard, Notifications);
    }

    private void ShowHot()
    {
        if (_user is null) return;
        CurrentPage = _hotPage ??= new HotViewModel(_hot, _picker, _store, Notifications);
    }

    private void ShowSettings()
    {
        if (_user is null) return;
        CurrentPage = _settings ??= new SettingsViewModel(
            _auth, _store, _appearance, Notifications, _user, ApplyBackground);
    }

    private void Logout()
    {
        _user = null;
        _dashboard = null;
        _doxPage = null;
        _hotPage = null;
        _settings = null;
        CurrentPage = null;
        LoginPage.Reset();
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(UserInitial));
        OnPropertyChanged(nameof(RootContent));
    }

    private void ApplyBackground(string? fileName) => _ = ApplyBackgroundAsync(fileName);

    private async Task ApplyBackgroundAsync(string? fileName)
        => Background = await BackgroundCatalog.LoadAsync(fileName, BackgroundCatalog.WallpaperWidth);
}
