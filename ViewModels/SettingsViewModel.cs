using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Animus.Common;
using Animus.Models;
using Animus.Services;

namespace Animus.ViewModels;

/// <summary>Uma aba da tela de configuracoes.</summary>
public sealed class SettingsTab : ViewModelBase
{
    private bool _isSelected;

    public SettingsTab(string title, string icon, ViewModelBase content, Action<SettingsTab> onSelect)
    {
        Title = title;
        Icon = icon;
        Content = content;
        SelectCommand = new RelayCommand(() => onSelect(this));
    }

    public string Title { get; }
    public string Icon { get; }
    public ViewModelBase Content { get; }
    public ICommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Tela de configuracoes: so segura as abas.</summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private const string IconAccount = "M12,17A2,2 0 0,0 14,15C14,13.89 13.1,13 12,13A2,2 0 0,0 10,15A2,2 0 0,0 12,17M18,8A2,2 0 0,1 20,10V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V10C4,8.89 4.9,8 6,8H7V6A5,5 0 0,1 12,1A5,5 0 0,1 17,6V8H18M12,3A3,3 0 0,0 9,6V8H15V6A3,3 0 0,0 12,3Z";
    private const string IconAppearance = "M12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2C17.5,2 22,6 22,10.5C22,13.5 19.5,16 16.5,16H14.7C14.4,16 14.2,16.2 14.2,16.5C14.2,16.6 14.3,16.7 14.3,16.8C14.7,17.3 14.9,17.9 14.9,18.5C15,19.9 13.8,22 12,22M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C12.3,20 12.5,19.8 12.5,19.5C12.5,19.3 12.4,19.2 12.3,19.1C11.9,18.6 11.7,18.1 11.7,17.5C11.7,16.1 12.9,15 14.2,15H16.5A6,6 0 0,0 20,10.5C20,7.1 16.4,4 12,4M6.5,10A1.5,1.5 0 0,1 8,11.5A1.5,1.5 0 0,1 6.5,13A1.5,1.5 0 0,1 5,11.5A1.5,1.5 0 0,1 6.5,10M9.5,6A1.5,1.5 0 0,1 11,7.5A1.5,1.5 0 0,1 9.5,9A1.5,1.5 0 0,1 8,7.5A1.5,1.5 0 0,1 9.5,6M14.5,6A1.5,1.5 0 0,1 16,7.5A1.5,1.5 0 0,1 14.5,9A1.5,1.5 0 0,1 13,7.5A1.5,1.5 0 0,1 14.5,6M17.5,10A1.5,1.5 0 0,1 19,11.5A1.5,1.5 0 0,1 17.5,13A1.5,1.5 0 0,1 16,11.5A1.5,1.5 0 0,1 17.5,10Z";
    private const string IconBackground = "M4,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M4,6V18H20V6H4M6,8H12V11H6V8Z";
    private const string IconNotifications = "M21,19V20H3V19L5,17V11C5,7.9 7.03,5.17 10,4.29C10,4.19 10,4.1 10,4A2,2 0 0,1 12,2A2,2 0 0,1 14,4C14,4.1 14,4.19 14,4.29C16.97,5.17 19,7.9 19,11V17L21,19M14,21A2,2 0 0,1 12,23A2,2 0 0,1 10,21";

    public SettingsViewModel(
        AuthService auth,
        AppDataStore store,
        AppearanceService appearance,
        NotificationService notifications,
        UserAccount user,
        Action<string?> onBackgroundChanged)
    {
        Tabs = new ObservableCollection<SettingsTab>
        {
            new("Conta", IconAccount, new AccountSettingsViewModel(auth, notifications, user), Select),
            new("Aparência", IconAppearance, new AppearanceSettingsViewModel(appearance), Select),
            new("Fundo", IconBackground, new BackgroundSettingsViewModel(store, onBackgroundChanged), Select),
            new("Notificações", IconNotifications, new NotificationsSettingsViewModel(notifications), Select),
        };

        Select(Tabs[0]);
    }

    public ObservableCollection<SettingsTab> Tabs { get; }

    private SettingsTab? _selectedTab;

    public SettingsTab? SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (SetProperty(ref _selectedTab, value))
                OnPropertyChanged(nameof(CurrentContent));
        }
    }

    public ViewModelBase? CurrentContent => SelectedTab?.Content;

    private void Select(SettingsTab tab)
    {
        foreach (var item in Tabs)
            item.IsSelected = ReferenceEquals(item, tab);

        SelectedTab = tab;
    }
}
