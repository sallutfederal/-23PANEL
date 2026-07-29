using System;
using System.Collections.Generic;
using System.Windows.Input;
using Animus.Common;
using Animus.Services;
using Avalonia;

namespace Animus.ViewModels;

/// <summary>Um campo do resultado: clicar copia o valor.</summary>
public sealed class DoxFieldViewModel
{
    public DoxFieldViewModel(DoxField field, Action<string> onCopy)
    {
        Label = field.Label;
        Value = field.Value;
        CopyCommand = new RelayCommand(() => onCopy(Value));
    }

    public string Label { get; }
    public string Value { get; }
    public ICommand CopyCommand { get; }
}

/// <summary>
/// Um bloco do resultado já achatado: título + campos, sem filhos.
/// A árvore vira uma lista única de blocos para a tela poder virtualizar
/// (só o que está à vista existe de verdade) — é isso que segura consultas
/// de nome com centenas de pessoas sem travar.
/// </summary>
public sealed class DoxBlockViewModel
{
    private const double IndentStep = 14;
    private const int MaxIndentLevels = 3;

    public DoxBlockViewModel(DoxGroup group, int depth, Action<string> onCopy)
    {
        Title = group.Title;
        Depth = depth;

        var fields = new DoxFieldViewModel[group.Fields.Count];
        for (var i = 0; i < fields.Length; i++)
            fields[i] = new DoxFieldViewModel(group.Fields[i], onCopy);
        Fields = fields;

        Facts = BlockFacts.FromFields(group.Fields);

        var left = Math.Min(depth, MaxIndentLevels) * IndentStep;
        Margin = new Thickness(left, 0, 0, depth == 0 ? 14 : 8);
    }

    public string? Title { get; }

    public bool HasTitle => !string.IsNullOrEmpty(Title);

    public int Depth { get; }

    /// <summary>Bloco de topo (CADASTRO, RESULTADO 3...): título em destaque, sem caixa.</summary>
    public bool IsSection => Depth == 0;

    public IReadOnlyList<DoxFieldViewModel> Fields { get; }

    public bool HasFields => Fields.Count > 0;

    public Thickness Margin { get; }

    /// <summary>O que dá pra filtrar neste bloco (estado, cidade, idade...).</summary>
    public BlockFacts Facts { get; }
}

/// <summary>
/// Um registro do resultado: um bloco de topo (uma pessoa, uma empresa, um veículo)
/// com tudo que pende dele. O filtro decide registro por registro.
/// </summary>
public sealed class DoxRecord
{
    public DoxRecord(IReadOnlyList<DoxBlockViewModel> blocks, bool isMeta)
    {
        Blocks = blocks;
        IsMeta = isMeta;

        var facts = new BlockFacts();
        foreach (var b in blocks) facts.Merge(b.Facts);
        Facts = facts;
    }

    public IReadOnlyList<DoxBlockViewModel> Blocks { get; }

    /// <summary>O cabeçalho "CONSULTA": fica sempre, filtro nenhum o esconde.</summary>
    public bool IsMeta { get; }

    /// <summary>Os fatos do registro inteiro (junta os de todos os blocos).</summary>
    public BlockFacts Facts { get; }

    public bool ContainsText(string lowerTerm)
    {
        foreach (var b in Blocks)
            if (b.Facts.Text.Contains(lowerTerm, StringComparison.Ordinal)) return true;
        return false;
    }
}
