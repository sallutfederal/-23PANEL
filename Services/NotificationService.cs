using Animus.Models;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;

namespace Animus.Services;

/// <summary>
/// Avisos do app. As preferencias dizem o que deve aparecer; os processos que forem
/// adicionados no futuro so precisam chamar <see cref="ProcessFinished"/> / <see cref="ProcessFailed"/>.
/// </summary>
public sealed class NotificationService
{
    private readonly AppDataStore _store;
    private WindowNotificationManager? _manager;

    public NotificationService(AppDataStore store) => _store = store;

    public NotificationPrefs Prefs => _store.Data.Notifications;

    /// <summary>Liga o servico a janela — chamado quando a janela carrega.</summary>
    public void Attach(TopLevel topLevel)
    {
        _manager = new WindowNotificationManager(topLevel)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 3,
        };
    }

    public void ProcessFinished(string title, string message)
    {
        if (!Prefs.OnProcessFinished) return;
        Show(title, message, NotificationType.Success);
    }

    public void ProcessFailed(string title, string message)
    {
        if (!Prefs.OnProcessFailed) return;
        Show(title, message, NotificationType.Error);
    }

    public void SettingsSaved(string message)
    {
        if (!Prefs.OnSettingsSaved) return;
        Show("Configurações", message, NotificationType.Success);
    }

    public void Show(string title, string message, NotificationType type = NotificationType.Information)
        => _manager?.Show(new Notification(title, message, type));

    public void Save() => _store.Save();
}
