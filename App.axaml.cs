using Animus.Services;
using Animus.ViewModels;
using Animus.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Animus;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new AppDataStore();
            var auth = new AuthService(store);
            var appearance = new AppearanceService(store);
            var notifications = new NotificationService(store);
            var dox = new DoxService();
            var clipboard = new ClipboardService();
            var hot = new HotService();
            var picker = new FilePickerService();

            // Cor, fonte, tamanhos e opacidade salvos entram antes da janela aparecer.
            appearance.ApplyAll();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(auth, store, appearance, notifications, dox, clipboard, hot, picker),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
