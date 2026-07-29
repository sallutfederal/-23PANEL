using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Animus.Common;
using Animus.Services;
using Avalonia.Media.Imaging;

namespace Animus.ViewModels;

/// <summary>Um fundo disponivel na tela de configuracoes.</summary>
public sealed class BackgroundOption : ViewModelBase
{
    private bool _isSelected;
    private Bitmap? _thumbnail;
    private bool _isLoading = true;

    public BackgroundOption(string fileName, Action<BackgroundOption> onSelect)
    {
        FileName = fileName;
        Label = BackgroundCatalog.LabelFor(fileName);
        SelectCommand = new RelayCommand(() => onSelect(this));
        _ = LoadThumbnailAsync();
    }

    public string FileName { get; }

    public string Label { get; }

    public ICommand SelectCommand { get; }

    /// <summary>Miniatura decodificada em segundo plano; ate chegar, o card mostra um placeholder.</summary>
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private async Task LoadThumbnailAsync()
    {
        // O await volta para a thread da UI (o VM e criado nela), entao pode atribuir direto.
        Thumbnail = await BackgroundCatalog.LoadAsync(FileName, BackgroundCatalog.ThumbnailWidth);
        IsLoading = false;
    }
}
