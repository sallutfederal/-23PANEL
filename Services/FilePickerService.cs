using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Animus.Services;

/// <summary>
/// Abre os diálogos de "escolher arquivo" e "salvar arquivo". Ligado à janela
/// no carregamento (o seletor de arquivos precisa do TopLevel), igual ao clipboard.
/// </summary>
public sealed class FilePickerService
{
    private TopLevel? _top;

    public void Attach(TopLevel topLevel) => _top = topLevel;

    /// <summary>Escolhe um arquivo existente (ex.: a lista de contas). Devolve o caminho ou null.</summary>
    public async Task<string?> PickOpenAsync(string title)
    {
        if (_top is null) return null;
        var files = await _top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Texto") { Patterns = new[] { "*.txt", "*.csv", "*.list" } },
                FilePickerFileTypes.All,
            },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Escolhe onde salvar (ex.: o relatório). Devolve o caminho ou null.</summary>
    public async Task<string?> PickSaveAsync(string title, string suggestedName)
    {
        if (_top is null) return null;
        var file = await _top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "jsonl",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Relatório JSONL") { Patterns = new[] { "*.jsonl", "*.json", "*.txt" } },
            },
        });
        return file?.TryGetLocalPath();
    }
}
