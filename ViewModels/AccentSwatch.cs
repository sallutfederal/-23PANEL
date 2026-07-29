using System;
using System.Windows.Input;
using Animus.Common;
using Avalonia.Media;

namespace Animus.ViewModels;

/// <summary>Uma cor pronta na paleta da aba Aparência.</summary>
public sealed class AccentSwatch : ViewModelBase
{
    private bool _isSelected;

    public AccentSwatch(string hex, Action<string> onSelect)
    {
        Hex = hex;
        Brush = Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : Brushes.Gray;
        SelectCommand = new RelayCommand(() => onSelect(hex));
    }

    public string Hex { get; }
    public IBrush Brush { get; }
    public ICommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
