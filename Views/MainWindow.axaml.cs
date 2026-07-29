using Animus.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Animus.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Notifications.Attach(this);
            vm.AttachClipboard(this);
            vm.AttachStorage(this);
        }
    }
}
