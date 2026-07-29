using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Animus.Services;

/// <summary>
/// Lista e carrega as imagens de fundo embutidas em Assets/ (arquivos que comecam com "bg").
/// As imagens sao grandes (varios MB), entao a decodificacao acontece fora da thread da UI
/// e o resultado fica em cache — sem isso a tela congela a cada navegacao.
/// </summary>
public static class BackgroundCatalog
{
    /// <summary>Largura das miniaturas da tela de configuracoes.</summary>
    public const int ThumbnailWidth = 420;

    /// <summary>Largura do fundo aplicado na janela.</summary>
    public const int WallpaperWidth = 1600;

    private static readonly string AssemblyName =
        typeof(BackgroundCatalog).Assembly.GetName().Name ?? "ANIMUS";

    private static readonly Uri AssetsFolder = new($"avares://{AssemblyName}/Assets");

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

    /// <summary>Nomes dos arquivos de fundo, em ordem natural (bg1, bg2, bg10).</summary>
    public static IReadOnlyList<string> List()
    {
        try
        {
            return AssetLoader.GetAssets(AssetsFolder, null)
                .Select(uri => Path.GetFileName(uri.AbsolutePath))
                .Where(IsBackground)
                .OrderBy(NumberIn)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsBackground(string fileName)
        => fileName.StartsWith("bg", StringComparison.OrdinalIgnoreCase)
           && ImageExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    private static int NumberIn(string fileName)
    {
        var digits = new string(Path.GetFileNameWithoutExtension(fileName).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
    }

    /// <summary>Rotulo amigavel: "bg2.png" vira "Fundo 2".</summary>
    public static string LabelFor(string fileName)
    {
        var number = NumberIn(fileName);
        return number == int.MaxValue
            ? Path.GetFileNameWithoutExtension(fileName)
            : $"Fundo {number}";
    }

    /// <summary>Carrega (fora da thread da UI) a imagem reduzida para a largura pedida.</summary>
    public static Task<Bitmap?> LoadAsync(string? fileName, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return Task.FromResult<Bitmap?>(null);

        var key = $"{fileName}@{decodeWidth}";
        if (Cache.TryGetValue(key, out var cached)) return Task.FromResult<Bitmap?>(cached);

        return Task.Run(() => Decode(fileName, decodeWidth, key));
    }

    /// <summary>Decodifica as miniaturas em segundo plano para a tela de configuracoes abrir instantanea.</summary>
    public static void Prewarm()
    {
        var files = List();
        _ = Task.Run(() =>
        {
            foreach (var file in files)
                Decode(file, ThumbnailWidth, $"{file}@{ThumbnailWidth}");
        });
    }

    private static Bitmap? Decode(string fileName, int decodeWidth, string key)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://{AssemblyName}/Assets/{fileName}"));
            var bitmap = Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality);
            return Cache.GetOrAdd(key, bitmap);
        }
        catch
        {
            return null;
        }
    }
}
