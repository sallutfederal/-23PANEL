using Animus.Models;
using Animus.Services;

namespace Animus.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    public DashboardViewModel(UserAccount user, AppearanceService appearance)
    {
        UserName = user.DisplayName;
        AnimationsEnabled = appearance.Data.Animations;
    }

    public string UserName { get; }

    public string Greeting => "Bem-vindo,";

    public string Initial => UserName.Length > 0 ? UserName[..1].ToUpperInvariant() : "?";

    public bool AnimationsEnabled { get; }
}
