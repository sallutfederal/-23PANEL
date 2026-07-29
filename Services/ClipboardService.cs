using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Animus.Services;

/// <summary>Copia texto para a area de transferencia. Ligado a janela no carregamento.</summary>
public sealed class ClipboardService
{
    private IClipboard? _clipboard;

    public void Attach(TopLevel topLevel) => _clipboard = topLevel.Clipboard;

    public async Task<bool> CopyAsync(string? text)
    {
        if (_clipboard is null || string.IsNullOrEmpty(text)) return false;
        await _clipboard.SetTextAsync(text);
        return true;
    }
}
