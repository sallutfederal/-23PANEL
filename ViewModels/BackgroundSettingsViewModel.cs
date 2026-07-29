using System;
using System.Collections.ObjectModel;
using System.Linq;
using Animus.Services;

namespace Animus.ViewModels;

/// <summary>Aba "Fundo": escolhe a única imagem de fundo do app.</summary>
public sealed class BackgroundSettingsViewModel : ViewModelBase
{
    private readonly AppDataStore _store;
    private readonly Action<string?> _onBackgroundChanged;

    public BackgroundSettingsViewModel(AppDataStore store, Action<string?> onBackgroundChanged)
    {
        _store = store;
        _onBackgroundChanged = onBackgroundChanged;

        var files = BackgroundCatalog.List();
        Backgrounds = new ObservableCollection<BackgroundOption>(
            files.Select(file => new BackgroundOption(file, Select)));

        var current = _store.Data.Background ?? files.FirstOrDefault();
        foreach (var option in Backgrounds)
            option.IsSelected = string.Equals(option.FileName, current, StringComparison.OrdinalIgnoreCase);
    }

    public ObservableCollection<BackgroundOption> Backgrounds { get; }

    public bool HasBackgrounds => Backgrounds.Count > 0;

    private void Select(BackgroundOption option)
    {
        foreach (var item in Backgrounds)
            item.IsSelected = ReferenceEquals(item, option);

        _store.Data.Background = option.FileName;
        _store.Save();
        _onBackgroundChanged(option.FileName);
    }
}
