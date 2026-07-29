using System;
using System.Collections.Generic;

namespace Animus.Services;

/// <summary>
/// Descobre o estado (UF) de um registro. A fonte nem sempre manda a UF: quando
/// não manda, dá pra deduzir do CEP ou do DDD, que são faixas fechadas por estado.
/// </summary>
public static class BrazilRegions
{
    private static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG",
        "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    };

    // Faixas de CEP por estado (comparadas pelos 5 primeiros dígitos).
    private static readonly (int From, int To, string Uf)[] CepRanges =
    {
        (1000, 19999, "SP"),  (20000, 28999, "RJ"), (29000, 29999, "ES"), (30000, 39999, "MG"),
        (40000, 48999, "BA"), (49000, 49999, "SE"), (50000, 56999, "PE"), (57000, 57999, "AL"),
        (58000, 58999, "PB"), (59000, 59999, "RN"), (60000, 63999, "CE"), (64000, 64999, "PI"),
        (65000, 65999, "MA"), (66000, 68899, "PA"), (68900, 68999, "AP"), (69000, 69299, "AM"),
        (69300, 69389, "RR"), (69390, 69899, "AM"), (69900, 69999, "AC"), (70000, 72799, "DF"),
        (72800, 72999, "GO"), (73000, 73699, "DF"), (73700, 76799, "GO"), (76800, 76999, "RO"),
        (77000, 77999, "TO"), (78000, 78899, "MT"), (78900, 78999, "RO"), (79000, 79999, "MS"),
        (80000, 87999, "PR"), (88000, 89999, "SC"), (90000, 99999, "RS"),
    };

    // DDD por estado.
    private static readonly Dictionary<int, string> DddMap = new()
    {
        [11] = "SP", [12] = "SP", [13] = "SP", [14] = "SP", [15] = "SP", [16] = "SP",
        [17] = "SP", [18] = "SP", [19] = "SP",
        [21] = "RJ", [22] = "RJ", [24] = "RJ", [27] = "ES", [28] = "ES",
        [31] = "MG", [32] = "MG", [33] = "MG", [34] = "MG", [35] = "MG", [37] = "MG", [38] = "MG",
        [41] = "PR", [42] = "PR", [43] = "PR", [44] = "PR", [45] = "PR", [46] = "PR",
        [47] = "SC", [48] = "SC", [49] = "SC",
        [51] = "RS", [53] = "RS", [54] = "RS", [55] = "RS",
        [61] = "DF", [62] = "GO", [63] = "TO", [64] = "GO", [65] = "MT", [66] = "MT",
        [67] = "MS", [68] = "AC", [69] = "RO",
        [71] = "BA", [73] = "BA", [74] = "BA", [75] = "BA", [77] = "BA", [79] = "SE",
        [81] = "PE", [82] = "AL", [83] = "PB", [84] = "RN", [85] = "CE", [86] = "PI",
        [87] = "PE", [88] = "CE", [89] = "PI",
        [91] = "PA", [92] = "AM", [93] = "PA", [94] = "PA", [95] = "RR", [96] = "AP",
        [97] = "AM", [98] = "MA", [99] = "MA",
    };

    /// <summary>"sp", " SP " e "São Paulo" -> "SP" quando for sigla; senão null.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length == 2 && All.Contains(v) ? v.ToUpperInvariant() : null;
    }

    /// <summary>
    /// A cidade vem como "CIDADE-UF" ("ARAGUAINA-TO"): pega a sigla do fim.
    /// Aceita "-", "/" e "," como separador.
    /// </summary>
    public static string? FromCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city)) return null;

        var v = city.Trim();
        var cut = v.LastIndexOfAny(new[] { '-', '/', ',' });
        return cut < 0 || cut == v.Length - 1 ? null : Normalize(v[(cut + 1)..]);
    }

    /// <summary>Estado a partir do CEP (aceita "01310-100", "01310100"...).</summary>
    public static string? FromCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep)) return null;

        var digits = 0;
        var taken = 0;
        foreach (var c in cep)
        {
            if (!char.IsDigit(c)) continue;
            digits = digits * 10 + (c - '0');
            if (++taken == 5) break;
        }
        if (taken < 5) return null;

        foreach (var (from, to, uf) in CepRanges)
            if (digits >= from && digits <= to) return uf;

        return null;
    }

    /// <summary>Estado a partir do DDD (aceita "11", "(11)", "011").</summary>
    public static string? FromDdd(string? ddd)
    {
        if (string.IsNullOrWhiteSpace(ddd)) return null;

        var digits = "";
        foreach (var c in ddd)
            if (char.IsDigit(c)) digits += c;

        // "011" -> "11"
        if (digits.Length == 3 && digits[0] == '0') digits = digits[1..];
        if (digits.Length != 2 || !int.TryParse(digits, out var code)) return null;

        return DddMap.TryGetValue(code, out var uf) ? uf : null;
    }
}
