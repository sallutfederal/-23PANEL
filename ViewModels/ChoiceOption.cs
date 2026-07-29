using System;
using System.Windows.Input;
using Animus.Common;
using Animus.Services;

namespace Animus.ViewModels;

/// <summary>Opcao selecionavel em grade (fonte, tamanho, intensidade...).</summary>
public sealed class ChoiceOption : ViewModelBase
{
    private bool _isSelected;

    public ChoiceOption(ChoiceInfo info, Action<ChoiceOption> onSelect)
    {
        Id = info.Id;
        Label = info.Label;
        Description = info.Description;
        SelectCommand = new RelayCommand(() => onSelect(this));
    }

    public string Id { get; }

    public string Label { get; }

    public string Description { get; }

    public ICommand SelectCommand { get; }

    /// <summary>Preenchido so nas opcoes de fonte: mostra o nome escrito na propria fonte.</summary>
    public Avalonia.Media.FontFamily? PreviewFont { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
