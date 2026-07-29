using System.Windows.Input;
using Animus.Common;
using Animus.Services;

namespace Animus.ViewModels;

/// <summary>Aba "Notificações": quando o app deve avisar.</summary>
public sealed class NotificationsSettingsViewModel : ViewModelBase
{
    private readonly NotificationService _notifications;

    public NotificationsSettingsViewModel(NotificationService notifications)
    {
        _notifications = notifications;
        TestCommand = new RelayCommand(Test);
    }

    public ICommand TestCommand { get; }

    /// <summary>Avisa quando um processo termina (os processos virão depois).</summary>
    public bool OnProcessFinished
    {
        get => _notifications.Prefs.OnProcessFinished;
        set
        {
            if (_notifications.Prefs.OnProcessFinished == value) return;
            _notifications.Prefs.OnProcessFinished = value;
            _notifications.Save();
            OnPropertyChanged();
        }
    }

    public bool OnProcessFailed
    {
        get => _notifications.Prefs.OnProcessFailed;
        set
        {
            if (_notifications.Prefs.OnProcessFailed == value) return;
            _notifications.Prefs.OnProcessFailed = value;
            _notifications.Save();
            OnPropertyChanged();
        }
    }

    public bool OnSettingsSaved
    {
        get => _notifications.Prefs.OnSettingsSaved;
        set
        {
            if (_notifications.Prefs.OnSettingsSaved == value) return;
            _notifications.Prefs.OnSettingsSaved = value;
            _notifications.Save();
            OnPropertyChanged();
        }
    }

    private void Test() => _notifications.ProcessFinished(
        "Processo concluído",
        "É assim que os avisos vão aparecer quando um processo terminar.");
}
