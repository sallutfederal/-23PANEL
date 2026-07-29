using System;
using System.IO;
using System.Text.Json;
using Animus.Models;

namespace Animus.Services;

/// <summary>Le e grava o arquivo de configuracao do app (senhas + fundo escolhido).</summary>
public sealed class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppDataStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ANIMUS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "config.json");
        Data = Load();
    }

    public AppData Data { get; private set; }

    private AppData Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new AppData();
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppData>(json) ?? new AppData();
        }
        catch
        {
            // Arquivo corrompido: recomeca do zero em vez de derrubar o app.
            return new AppData();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Data, JsonOptions));
        }
        catch
        {
            // Sem permissao de escrita: mantem o estado apenas em memoria.
        }
    }
}
