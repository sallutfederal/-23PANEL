using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Animus.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        LoginBox.Focus();
    }
}
