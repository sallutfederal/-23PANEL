using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Animus.Services;

/// <summary>Um campo mostrado no resultado (rótulo + valor), copiável ao clicar.</summary>
public sealed record DoxField(string Label, string Value);

/// <summary>Um bloco do resultado (ex.: "Cadastro", "Telefone 1") com seus campos e sub-blocos.</summary>
public sealed class DoxGroup
{
    public string? Title { get; init; }

    /// <summary>Bloco de serviço (o "CONSULTA"), não é um registro do resultado.</summary>
    public bool IsMeta { get; init; }

    public List<DoxField> Fields { get; } = new();

    public List<DoxGroup> SubGroups { get; } = new();

    /// <summary>Blocos vazios nunca sao guardados, entao isso e so uma checagem barata.</summary>
    public bool IsEmpty => Fields.Count == 0 && SubGroups.Count == 0;
}

public sealed class DoxResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<DoxGroup> Groups { get; init; } = Array.Empty<DoxGroup>();

    /// <summary>Quantos campos entraram no resultado (usado no resumo da tela).</summary>
    public int FieldCount { get; init; }

    /// <summary>A fonte devolveu mais do que os limites permitem e parte ficou de fora.</summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Faz a consulta do 23 DOX: GET na URL da opção, converte o JSON em blocos legíveis
/// (ignorando o que vier vazio) e monta um texto puro para copiar tudo de uma vez.
///
/// Consultas por nome podem devolver centenas de pessoas, então tudo aqui é feito
/// fora da thread da interface e com tetos: a tela nunca fica travada esperando.
/// </summary>
public sealed class DoxService
{
    /// <summary>Acima disso a resposta é recusada em vez de estourar a memória.</summary>
    private const int MaxResponseBytes = 32 * 1024 * 1024;

    /// <summary>Teto de campos montados a partir de uma resposta.</summary>
    private const int MaxFields = 40_000;

    /// <summary>Teto de itens lidos de uma mesma lista (ex.: 400 pessoas de um nome).</summary>
    private const int MaxArrayItems = 400;

    /// <summary>Teto de aninhamento: JSON muito fundo vira ruído na tela.</summary>
    private const int MaxDepth = 8;

    /// <summary>Valor gigante quebra o layout do campo; corta e sinaliza com reticências.</summary>
    private const int MaxValueChars = 400;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // Rótulos amigáveis para as chaves conhecidas do JSON.
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IdUnico"] = "ID", ["Doc"] = "CPF", ["Cpf"] = "CPF", ["Nome"] = "NOME", ["Genero"] = "GÊNERO",
        ["DataNascimento"] = "DATA DE NASCIMENTO", ["NomeMae"] = "NOME DA MÃE", ["NomePai"] = "NOME DO PAI",
        ["TituloEleitor"] = "TÍTULO DE ELEITOR", ["Obito"] = "ÓBITO", ["Rg"] = "RG", ["Pis"] = "PIS",
        ["CBO"] = "CBO", ["Renda"] = "RENDA", ["Email"] = "EMAIL", ["Email2"] = "EMAIL 2", ["Signo"] = "SIGNO",
        ["Logradouro"] = "LOGRADOURO", ["Numero"] = "NÚMERO", ["Complemento"] = "COMPLEMENTO",
        ["Bairro"] = "BAIRRO", ["Cidade"] = "CIDADE", ["Cep"] = "CEP", ["DDD"] = "DDD", ["Telefone"] = "TELEFONE",
        ["Operadora"] = "OPERADORA", ["TipoTelefone"] = "TIPO", ["Whatsapp"] = "WHATSAPP",
        ["NaoPerturbe"] = "NÃO PERTURBE", ["Rcs"] = "RCS", ["BloqueioLgpd"] = "BLOQUEIO LGPD",
        ["PercentualHot"] = "PERCENTUAL", ["ClassificacaoGeral"] = "CLASSIFICAÇÃO",
        ["consulta"] = "CONSULTA", ["Telefones"] = "TELEFONES", ["Enderecos"] = "ENDEREÇOS",
        ["Veiculos"] = "VEÍCULOS", ["Vinculos"] = "VÍNCULOS",
        ["ParticipacaoQuadroSocietario"] = "QUADRO SOCIETÁRIO", ["Cadastro"] = "CADASTRO",
    };

    // Singular para nomear itens de listas ("Telefones" -> "TELEFONE 1").
    private static readonly Dictionary<string, string> Singular = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Telefones"] = "TELEFONE", ["Enderecos"] = "ENDEREÇO", ["Veiculos"] = "VEÍCULO",
        ["Vinculos"] = "VÍNCULO", ["Emails"] = "EMAIL", ["Cadastro"] = "CADASTRO",
        ["response"] = "RESULTADO", ["Resultados"] = "RESULTADO", ["Pessoas"] = "PESSOA",
    };

    // Chaves de topo que não interessam mostrar.
    private static readonly HashSet<string> SkipKeys = new(StringComparer.OrdinalIgnoreCase) { "ok", "response" };

    // Chaves que a fonte manda mas não viram campo na tela, em nenhum nível
    // ("owner" é só a conta do token — não é dado da consulta).
    private static readonly HashSet<string> HiddenKeys = new(StringComparer.OrdinalIgnoreCase) { "owner" };

    public async Task<DoxResult> QueryAsync(DoxOption option, string value, CancellationToken ct = default)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0)
            return Fail("Digite um valor para consultar.");

        var url = option.UrlTemplate.Replace("{valor}", Uri.EscapeDataString(v));

        byte[] payload;
        try
        {
            using var resp = await Http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                return Fail($"A fonte respondeu {(int)resp.StatusCode}.");

            if (resp.Content.Headers.ContentLength is > MaxResponseBytes)
                return Fail("A resposta veio grande demais. Refine a busca (nome completo, por exemplo).");

            payload = await ReadCappedAsync(resp.Content, ct).ConfigureAwait(false);
            if (payload.Length == 0)
                return Fail("A fonte devolveu uma resposta vazia.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Fail("Consulta cancelada.");
        }
        catch (OperationCanceledException)
        {
            return Fail("A consulta demorou demais e foi interrompida.");
        }
        catch (InvalidDataException)
        {
            return Fail("A resposta veio grande demais. Refine a busca (nome completo, por exemplo).");
        }
        catch (Exception ex)
        {
            return Fail($"Falha na consulta: {ex.Message}");
        }

        // Parse fora da thread da interface: JSON grande nao pode segurar a tela.
        return await Task.Run(() => Parse(payload), ct).ConfigureAwait(false);
    }

    /// <summary>Lê o corpo com teto de tamanho, sem carregar tudo de uma vez na memória.</summary>
    private static async Task<byte[]> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream(64 * 1024);
        var chunk = new byte[64 * 1024];

        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
                throw new InvalidDataException("resposta acima do teto");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Converte o JSON em blocos. Público para testar sem rede.</summary>
    public static DoxResult Parse(string json) => Parse(Encoding.UTF8.GetBytes(json));

    public static DoxResult Parse(ReadOnlyMemory<byte> utf8Json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8Json); }
        catch { return Fail("A fonte não retornou um JSON válido."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail("Formato inesperado na resposta.");

            // Se a fonte sinalizou erro (ok:false), respeita.
            if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
            {
                var msg = root.TryGetProperty("erro", out var e) || root.TryGetProperty("message", out e)
                    ? e.GetString() : "A consulta não encontrou resultados.";
                return Fail(msg ?? "A consulta não encontrou resultados.");
            }

            var budget = new Budget();
            var top = new DoxGroup();

            foreach (var prop in root.EnumerateObject())
            {
                if (SkipKeys.Contains(prop.Name)) continue;
                AddValue(top, prop.Name, prop.Value, 0, budget);
            }

            // O grosso dos dados vem em response{...}: seus filhos sobem para o topo
            // (sem criar um bloco "RESPONSE" a mais).
            if (root.TryGetProperty("response", out var response))
            {
                if (response.ValueKind == JsonValueKind.Object)
                    foreach (var p in response.EnumerateObject())
                        AddValue(top, p.Name, p.Value, 0, budget);
                else
                    AddValue(top, "response", response, 0, budget);
            }

            var groups = new List<DoxGroup>();
            // Campos soltos do topo (consulta, fonte, cpf...) viram uma seção "CONSULTA".
            if (top.Fields.Count > 0)
            {
                var consulta = new DoxGroup { Title = "CONSULTA", IsMeta = true };
                consulta.Fields.AddRange(top.Fields);
                groups.Add(consulta);
            }
            groups.AddRange(top.SubGroups);

            return new DoxResult
            {
                Ok = true,
                Groups = groups,
                FieldCount = budget.Fields,
                Truncated = budget.Truncated,
            };
        }
    }

    private static DoxResult Fail(string error) => new() { Ok = false, Error = error };

    // ---- construção recursiva dos blocos ----
    private static void AddValue(DoxGroup target, string key, JsonElement value, int depth, Budget budget)
    {
        if (budget.Full) { budget.Truncated = true; return; }
        if (HiddenKeys.Contains(key)) return;

        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return;

            case JsonValueKind.String:
                var s = value.GetString();
                if (!string.IsNullOrWhiteSpace(s)) budget.Add(target, Label(key), Clip(s!));
                return;

            case JsonValueKind.Number:
                budget.Add(target, Label(key), value.GetRawText());
                return;

            case JsonValueKind.True:
            case JsonValueKind.False:
                budget.Add(target, Label(key), value.ValueKind == JsonValueKind.True ? "Sim" : "Não");
                return;

            case JsonValueKind.Object:
                if (depth >= MaxDepth) return;
                var obj = new DoxGroup { Title = Label(key) };
                foreach (var p in value.EnumerateObject())
                    AddValue(obj, p.Name, p.Value, depth + 1, budget);
                if (!obj.IsEmpty) target.SubGroups.Add(obj);
                return;

            case JsonValueKind.Array:
                if (depth >= MaxDepth) return;
                AddArray(target, key, value, depth, budget);
                return;
        }
    }

    private static void AddArray(DoxGroup target, string key, JsonElement array, int depth, Budget budget)
    {
        var count = array.GetArrayLength();
        if (count == 0) return;

        // Array de valores simples -> lista separada por vírgula.
        var onlyScalars = true;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array) { onlyScalars = false; break; }
        }

        if (onlyScalars)
        {
            var sb = new StringBuilder();
            var used = 0;
            foreach (var item in array.EnumerateArray())
            {
                if (used >= MaxArrayItems) { budget.Truncated = true; break; }
                var text = Scalar(item);
                if (text.Length == 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(text);
                used++;
            }
            if (sb.Length > 0) budget.Add(target, Label(key), Clip(sb.ToString()));
            return;
        }

        // Array de objetos -> um sub-bloco por item ("TELEFONE 1", "TELEFONE 2"...).
        var singular = Singular.TryGetValue(key, out var sg) ? sg : Label(key).TrimEnd('S');
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (index >= MaxArrayItems || budget.Full) { budget.Truncated = true; break; }
            index++;

            if (item.ValueKind != JsonValueKind.Object)
            {
                AddValue(target, singular, item, depth + 1, budget);
                continue;
            }

            var group = new DoxGroup { Title = count > 1 ? $"{singular} {index}" : singular };
            foreach (var p in item.EnumerateObject())
                AddValue(group, p.Name, p.Value, depth + 1, budget);
            if (!group.IsEmpty) target.SubGroups.Add(group);
        }
    }

    private static string Scalar(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString()?.Trim() ?? "",
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "Sim",
        JsonValueKind.False => "Não",
        _ => "",
    };

    private static string Clip(string value)
    {
        var v = value.Trim();
        return v.Length <= MaxValueChars ? v : string.Concat(v.AsSpan(0, MaxValueChars), "…");
    }

    private static string Label(string key)
    {
        if (Labels.TryGetValue(key, out var l)) return l;
        // desconhecida: quebra camelCase e sobe pra maiúsculas ("DataX" -> "DATA X")
        var sb = new StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(key[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().ToUpperInvariant();
    }

    /// <summary>Controla quantos campos ja entraram, para nao montar uma tela infinita.</summary>
    private sealed class Budget
    {
        public int Fields;
        public bool Truncated;

        public bool Full => Fields >= MaxFields;

        public void Add(DoxGroup group, string label, string value)
        {
            if (Full) { Truncated = true; return; }
            group.Fields.Add(new DoxField(label, value));
            Fields++;
        }
    }

}
