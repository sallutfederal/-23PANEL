using System;
using System.Collections.Generic;
using System.Linq;
using Animus.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Animus.Services;

public sealed record ChoiceInfo(string Id, string Label, string Description);

/// <summary>Limites de cada slider da aba Aparencia.</summary>
public readonly record struct Range(double Min, double Max, double Default);

/// <summary>
/// Aplica toda a personalizacao (cor, fonte, tamanhos, cantos, opacidade, efeitos) trocando os
/// recursos da aplicacao. Como os estilos usam DynamicResource, a mudanca aparece na hora.
/// </summary>
public sealed class AppearanceService
{
    // ---- opcoes / limites oferecidos na tela ----
    public static readonly IReadOnlyList<ChoiceInfo> Fonts = new[]
    {
        new ChoiceInfo("Sora", "Sora", "Geométrica e elegante"),
        new ChoiceInfo("Manrope", "Manrope", "Suave e moderna"),
        new ChoiceInfo("Plus Jakarta Sans", "Jakarta", "Limpa e atual"),
        new ChoiceInfo("Figtree", "Figtree", "Arredondada e amigável"),
        new ChoiceInfo("DM Sans", "DM Sans", "Neutra e legível"),
        new ChoiceInfo("Outfit", "Outfit", "Fina e minimalista"),
        new ChoiceInfo("Kanit", "Kanit", "Compacta e forte"),
        new ChoiceInfo("Chakra Petch", "Chakra", "Técnica e quadrada"),
    };

    /// <summary>Cores prontas para os swatches (a cor livre vem do seletor).</summary>
    public static readonly IReadOnlyList<string> AccentSwatches = new[]
    {
        "#2ee27a", "#37d6c3", "#41b6ff", "#7b8cff", "#a97bff",
        "#ff6bd0", "#ff5470", "#ff9a5c", "#ffca45", "#c6e84f", "#d7dee8",
    };

    public static readonly Range FontScaleRange = new(0.85, 1.25, 1.0);
    public static readonly Range PanelOpacityRange = new(0.30, 1.0, 0.78);
    public static readonly Range BackgroundDimRange = new(0.20, 1.0, 0.50);
    public static readonly Range CornerRange = new(0, 24, 13);
    public static readonly Range PaddingRange = new(14, 36, 24);
    public static readonly Range ControlScaleRange = new(0.85, 1.25, 1.0);

    // Tamanhos base de texto (escala 1.0); ControlScale mexe so nas abas/botoes.
    private static readonly (string Key, double Size)[] FontTokens =
    {
        ("FsMicro", 10.5), ("FsLabel", 11), ("FsSmall", 11.5), ("FsSubtitle", 12.5),
        ("FsBody", 13.5), ("FsTitle", 15.5), ("FsBrand", 14), ("FsGreeting", 19),
        ("FsH1", 25), ("FsLoginTitle", 26), ("FsAvatar", 34), ("FsHero", 44),
    };

    private readonly AppDataStore _store;

    public AppearanceService(AppDataStore store) => _store = store;

    public AppearanceData Data => _store.Data.Appearance;

    /// <summary>Disparado quando algo muda (a barra lateral usa para ligar/desligar animacoes).</summary>
    public event Action? Changed;

    // ------------------------------------------------------------------
    public void ApplyAll()
    {
        ApplyAccent(Data.Accent);
        ApplyFont(Data.FontFamily);
        ApplyFontScale(Data.FontScale);
        ApplyOpacity(Data.PanelOpacity);
        ApplyDim(Data.BackgroundDim);
        ApplyCorner(Data.CornerRadius);
        ApplyPadding(Data.CardPadding);
        ApplyControlScale(Data.ControlScale);
        ApplyShadows(Data.Shadows);
    }

    public void SetAccent(string hex)
    {
        if (!Color.TryParse(hex, out _)) return;
        Data.Accent = hex;
        ApplyAccent(hex);
        SaveAndNotify();
    }

    public void SetFont(string family) { Data.FontFamily = family; ApplyFont(family); SaveAndNotify(); }
    public void SetFontScale(double v) { Data.FontScale = v; ApplyFontScale(v); SaveAndNotify(); }
    public void SetPanelOpacity(double v) { Data.PanelOpacity = v; ApplyOpacity(v); SaveAndNotify(); }
    public void SetBackgroundDim(double v) { Data.BackgroundDim = v; ApplyDim(v); SaveAndNotify(); }
    public void SetCorner(double v) { Data.CornerRadius = v; ApplyCorner(v); SaveAndNotify(); }
    public void SetPadding(double v) { Data.CardPadding = v; ApplyPadding(v); SaveAndNotify(); }
    public void SetControlScale(double v) { Data.ControlScale = v; ApplyControlScale(v); SaveAndNotify(); }
    public void SetShadows(bool on) { Data.Shadows = on; ApplyShadows(on); SaveAndNotify(); }
    public void SetAnimations(bool on) { Data.Animations = on; SaveAndNotify(); }

    private void SaveAndNotify()
    {
        _store.Save();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------
    private static IResourceDictionary? Res => Application.Current?.Resources;

    private static void ApplyAccent(string hex)
    {
        if (Res is null || !Color.TryParse(hex, out var a)) return;

        var accent = new SolidColorBrush(a);
        var light = new SolidColorBrush(Mix(a, Colors.White, 0.26));
        var dark = new SolidColorBrush(Scale(a, 0.78));

        Res["AnimusAccent"] = accent;
        Res["AnimusAccentLight"] = light;
        Res["AnimusAccentDark"] = dark;
        Res["AnimusAccentSoft"] = new SolidColorBrush(Color.FromArgb(0x29, a.R, a.G, a.B));
        Res["AnimusOnAccent"] = new SolidColorBrush(ReadableOn(a));
        Res["AnimusAccentGradient"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(a, 0), new GradientStop(Scale(a, 0.78), 1) },
        };

        // Slider do Fluent segue o accent (parte cheia + thumb).
        Res["SliderTrackValueFill"] = accent;
        Res["SliderTrackValueFillPointerOver"] = light;
        Res["SliderTrackValueFillPressed"] = dark;
        Res["SliderThumbBackground"] = accent;
        Res["SliderThumbBackgroundPointerOver"] = light;
        Res["SliderThumbBackgroundPressed"] = dark;
    }

    private static void ApplyFont(string family)
    {
        if (Res is null) return;
        var asm = typeof(AppearanceService).Assembly.GetName().Name;
        Res["UiFont"] = new FontFamily($"avares://{asm}/Assets/Fonts#{family}");
    }

    private static void ApplyFontScale(double scale)
    {
        if (Res is null) return;
        foreach (var (key, size) in FontTokens)
            Res[key] = Math.Round(size * scale, 1);
    }

    private static void ApplyOpacity(double opacity)
    {
        if (Res is null) return;
        var a = (byte)Math.Clamp(opacity * 255, 0, 255);
        var aUp = (byte)Math.Clamp(a + 12, 0, 255);   // paineis internos um pouco mais fechados
        var aField = (byte)Math.Clamp(a + 22, 0, 255); // campo de texto mais fechado p/ leitura
        Res["AnimusSidebar"] = Tint(aUp, 0x0d, 0x0f, 0x13);
        Res["AnimusPanel"] = Tint(a, 0x11, 0x14, 0x19);
        Res["AnimusPanel2"] = Tint(a, 0x16, 0x1a, 0x20);
        Res["AnimusPanel3"] = Tint(aUp, 0x1b, 0x20, 0x27);
        Res["AnimusField"] = Tint(aField, 0x0a, 0x0b, 0x0e);
        Res["TextControlBackground"] = Tint(aField, 0x0a, 0x0b, 0x0e);
        Res["TextControlBackgroundPointerOver"] = Tint(aField, 0x10, 0x13, 0x18);
        Res["TextControlBackgroundFocused"] = Tint(aField, 0x10, 0x13, 0x18);
    }

    private static void ApplyDim(double dim)
    {
        if (Res is null) return;
        var a = (byte)Math.Clamp(dim * 255, 0, 255);
        Res["AnimusScrim"] = Tint(a, 0x0a, 0x0b, 0x0e);
    }

    private static void ApplyCorner(double r)
    {
        if (Res is null) return;
        Res["RadCard"] = new CornerRadius(r);
        Res["RadControl"] = new CornerRadius(Math.Clamp(r - 3, 4, 14));
        Res["RadPill"] = new CornerRadius(r + 8);
        Res["RadThumb"] = new CornerRadius(Math.Clamp(r - 2, 6, 16));
    }

    private static void ApplyPadding(double p)
    {
        if (Res is null) return;
        Res["PadCard"] = new Thickness(p);
    }

    private static void ApplyControlScale(double scale)
    {
        if (Res is null) return;
        Res["FsTab"] = Math.Round(12.5 * scale, 1);
        Res["FsOption"] = Math.Round(13.5 * scale, 1);
        Res["PadTab"] = new Thickness(Math.Round(15 * scale), Math.Round(10 * scale));
        Res["PadOption"] = new Thickness(Math.Round(14 * scale), Math.Round(12 * scale));
    }

    private static void ApplyShadows(bool on)
    {
        if (Res is null) return;
        Res["CardShadow"] = on ? BoxShadows.Parse("0 18 40 -18 #cc000000") : default;
    }

    // ---- helpers de cor ----
    private static SolidColorBrush Tint(byte alpha, byte r, byte g, byte b) => new(Color.FromArgb(alpha, r, g, b));

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        a.A, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static Color Scale(Color c, double f) => Color.FromArgb(
        c.A, (byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    private static Color ReadableOn(Color c)
    {
        var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return lum > 0.55 ? Color.FromRgb(0x06, 0x14, 0x0d) : Colors.White;
    }
}
