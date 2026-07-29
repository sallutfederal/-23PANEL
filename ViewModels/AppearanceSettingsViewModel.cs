using System;
using System.Collections.ObjectModel;
using System.Linq;
using Animus.Services;
using Avalonia.Media;

namespace Animus.ViewModels;

/// <summary>Aba "Aparência": cor, fonte, tamanhos, cantos, opacidade e efeitos — tudo por slider/opção.</summary>
public sealed class AppearanceSettingsViewModel : ViewModelBase
{
    private readonly AppearanceService _appearance;
    private Color _accentColor;

    public AppearanceSettingsViewModel(AppearanceService appearance)
    {
        _appearance = appearance;

        Swatches = new ObservableCollection<AccentSwatch>(
            AppearanceService.AccentSwatches.Select(hex => new AccentSwatch(hex, SetAccent)));

        Fonts = new ObservableCollection<ChoiceOption>(
            AppearanceService.Fonts.Select(f => new ChoiceOption(f, o => SetFont(o.Id))));

        var asm = typeof(AppearanceService).Assembly.GetName().Name;
        foreach (var option in Fonts)
        {
            option.PreviewFont = new FontFamily($"avares://{asm}/Assets/Fonts#{option.Id}");
            option.IsSelected = string.Equals(option.Id, _appearance.Data.FontFamily, StringComparison.OrdinalIgnoreCase);
        }

        _accentColor = Color.TryParse(_appearance.Data.Accent, out var c) ? c : Colors.LimeGreen;
        SyncSwatches();
    }

    public ObservableCollection<AccentSwatch> Swatches { get; }
    public ObservableCollection<ChoiceOption> Fonts { get; }

    // ---- cor ----
    /// <summary>Ligado ao seletor de cor completo (ColorPicker).</summary>
    public Color AccentColor
    {
        get => _accentColor;
        set
        {
            if (!SetProperty(ref _accentColor, value)) return;
            _appearance.SetAccent(HexOf(value));
            SyncSwatches();
        }
    }

    private void SetAccent(string hex)
    {
        if (Color.TryParse(hex, out var c)) AccentColor = c; // dispara o setter acima
    }

    // ---- sliders (Min/Max expostos para o XAML) ----
    public double FontScaleMin => AppearanceService.FontScaleRange.Min;
    public double FontScaleMax => AppearanceService.FontScaleRange.Max;
    public double FontScale
    {
        get => _appearance.Data.FontScale;
        set { if (Different(value, _appearance.Data.FontScale)) { _appearance.SetFontScale(value); OnPropertyChanged(); OnPropertyChanged(nameof(FontScalePercent)); } }
    }
    public string FontScalePercent => Percent(FontScale);

    public double PanelOpacityMin => AppearanceService.PanelOpacityRange.Min;
    public double PanelOpacityMax => AppearanceService.PanelOpacityRange.Max;
    public double PanelOpacity
    {
        get => _appearance.Data.PanelOpacity;
        set { if (Different(value, _appearance.Data.PanelOpacity)) { _appearance.SetPanelOpacity(value); OnPropertyChanged(); OnPropertyChanged(nameof(PanelOpacityPercent)); } }
    }
    public string PanelOpacityPercent => Percent(PanelOpacity);

    public double BackgroundDimMin => AppearanceService.BackgroundDimRange.Min;
    public double BackgroundDimMax => AppearanceService.BackgroundDimRange.Max;
    /// <summary>Mostrado como "quanto do fundo aparece" = inverso do escurecimento.</summary>
    public double BackgroundShow
    {
        get => AppearanceService.BackgroundDimRange.Max + AppearanceService.BackgroundDimRange.Min - _appearance.Data.BackgroundDim;
        set
        {
            var dim = AppearanceService.BackgroundDimRange.Max + AppearanceService.BackgroundDimRange.Min - value;
            if (Different(dim, _appearance.Data.BackgroundDim)) { _appearance.SetBackgroundDim(dim); OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundShowPercent)); }
        }
    }
    public string BackgroundShowPercent => Percent((BackgroundShow - BackgroundDimMin) / (BackgroundDimMax - BackgroundDimMin));

    public double CornerMin => AppearanceService.CornerRange.Min;
    public double CornerMax => AppearanceService.CornerRange.Max;
    public double Corner
    {
        get => _appearance.Data.CornerRadius;
        set { if (Different(value, _appearance.Data.CornerRadius)) { _appearance.SetCorner(value); OnPropertyChanged(); OnPropertyChanged(nameof(CornerLabel)); } }
    }
    public string CornerLabel => $"{Math.Round(Corner)} px";

    public double PaddingMin => AppearanceService.PaddingRange.Min;
    public double PaddingMax => AppearanceService.PaddingRange.Max;
    public double CardPadding
    {
        get => _appearance.Data.CardPadding;
        set { if (Different(value, _appearance.Data.CardPadding)) { _appearance.SetPadding(value); OnPropertyChanged(); OnPropertyChanged(nameof(PaddingLabel)); } }
    }
    public string PaddingLabel => $"{Math.Round(CardPadding)} px";

    public double ControlScaleMin => AppearanceService.ControlScaleRange.Min;
    public double ControlScaleMax => AppearanceService.ControlScaleRange.Max;
    public double ControlScale
    {
        get => _appearance.Data.ControlScale;
        set { if (Different(value, _appearance.Data.ControlScale)) { _appearance.SetControlScale(value); OnPropertyChanged(); OnPropertyChanged(nameof(ControlScalePercent)); } }
    }
    public string ControlScalePercent => Percent(ControlScale);

    // ---- efeitos ----
    public bool Shadows
    {
        get => _appearance.Data.Shadows;
        set { if (value != _appearance.Data.Shadows) { _appearance.SetShadows(value); OnPropertyChanged(); } }
    }

    public bool Animations
    {
        get => _appearance.Data.Animations;
        set { if (value != _appearance.Data.Animations) { _appearance.SetAnimations(value); OnPropertyChanged(); } }
    }

    // ---- helpers ----
    private void SetFont(string id)
    {
        _appearance.SetFont(id);
        foreach (var option in Fonts)
            option.IsSelected = string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase);
    }

    private void SyncSwatches()
    {
        var hex = HexOf(_accentColor);
        foreach (var s in Swatches)
            s.IsSelected = string.Equals(s.Hex, hex, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Different(double a, double b) => Math.Abs(a - b) > 0.0001;
    private static string Percent(double v) => $"{Math.Round(v * 100)}%";
    private static string HexOf(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
