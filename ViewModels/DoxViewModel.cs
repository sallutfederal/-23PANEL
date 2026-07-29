using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Animus.Common;
using Animus.Services;

namespace Animus.ViewModels;

/// <summary>
/// Aba "23 DOX": escolhe a consulta, faz o GET e mostra o resultado copiável.
///
/// Consulta de nome pode voltar com centenas de pessoas. Para a tela não travar:
/// a rede, o parse do JSON, a montagem dos blocos e o texto do "copiar tudo"
/// acontecem fora da thread da interface, e o resultado vira uma lista achatada
/// entregue de uma vez só (a tela virtualiza: só o que está à vista existe).
/// </summary>
public sealed class DoxViewModel : ViewModelBase
{
    private readonly DoxService _dox;
    private readonly ClipboardService _clipboard;
    private readonly NotificationService _notifications;

    /// <summary>Consultas que não valem filtro: uma só pessoa volta, não há o que peneirar.</summary>
    private static readonly string[] NoFilterQueries = { "cpf" };

    private IReadOnlyList<DoxBlockViewModel> _blocks = Array.Empty<DoxBlockViewModel>();
    private IReadOnlyList<DoxRecord> _records = Array.Empty<DoxRecord>();
    private DoxOption? _resultOption;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _filterCts;
    private int _fieldCount;
    private int _recordCount;
    private int _visibleCount;
    private bool _copying;

    private DoxOption _selectedOption;
    private string _query = "";
    private bool _isLoading;
    private string _errorMessage = "";
    private bool _hasResult;
    private bool _truncated;

    public DoxViewModel(DoxService dox, ClipboardService clipboard, NotificationService notifications)
    {
        _dox = dox;
        _clipboard = clipboard;
        _notifications = notifications;

        Options = new ObservableCollection<DoxOption>(DoxCatalog.Options);
        _selectedOption = Options[0];

        Filter = new DoxFilterViewModel();
        Filter.Changed += OnFilterChanged;

        ConsultarCommand = new AsyncRelayCommand(ConsultarAsync);
        CancelarCommand = new RelayCommand(Cancelar);
        CopyAllCommand = new RelayCommand(CopyAll);
    }

    public ObservableCollection<DoxOption> Options { get; }

    /// <summary>A barra de filtros do resultado (estado, cidade, idade, gênero...).</summary>
    public DoxFilterViewModel Filter { get; }

    /// <summary>
    /// O resultado inteiro, já achatado. Trocado de uma vez só: um aviso de
    /// mudança, um layout — em vez de milhares de inserções uma a uma.
    /// </summary>
    public IReadOnlyList<DoxBlockViewModel> Blocks
    {
        get => _blocks;
        private set
        {
            if (!SetProperty(ref _blocks, value)) return;
            OnPropertyChanged(nameof(Summary));
        }
    }

    public ICommand ConsultarCommand { get; }
    public ICommand CancelarCommand { get; }
    public ICommand CopyAllCommand { get; }

    public DoxOption SelectedOption
    {
        get => _selectedOption;
        set
        {
            // A ComboBox devolve null em alguns momentos (troca de itens): manter a
            // última escolha em vez de ficar sem consulta selecionada.
            if (value is null) return;
            if (SetProperty(ref _selectedOption, value))
                OnPropertyChanged(nameof(Placeholder));
        }
    }

    public string Placeholder => SelectedOption.Placeholder;

    public string Query
    {
        get => _query;
        set => SetProperty(ref _query, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    /// <summary>A fonte devolveu mais do que cabe na tela e parte foi omitida.</summary>
    public bool IsTruncated
    {
        get => _truncated;
        private set => SetProperty(ref _truncated, value);
    }

    /// <summary>
    /// Mostra o botão de filtros. Fica de fora quando a consulta não pede filtro
    /// (CPF traz uma pessoa só) ou quando não há nada peneirável no resultado.
    /// </summary>
    public bool CanFilter => HasResult
                             && _resultOption is not null
                             && Array.IndexOf(NoFilterQueries, _resultOption.Id) < 0
                             && (Filter.IsAvailable || _recordCount > 1);

    /// <summary>O filtro escondeu tudo: o resultado existe, mas nada bate.</summary>
    public bool IsEmptyByFilter => HasResult && _recordCount > 0 && _visibleCount == 0;

    public string Summary => _recordCount == 0
        ? "clique em qualquer campo para copiar"
        : Filter.IsActive
            ? $"{_visibleCount} de {Plural(_recordCount)} · filtro ativo"
            : $"{Plural(_recordCount)} · {_fieldCount} campos · clique em qualquer campo para copiar";

    private static string Plural(int records) => records == 1 ? "1 registro" : $"{records} registros";

    private async Task ConsultarAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;

        IsLoading = true;
        ErrorMessage = "";
        ClearResult();

        try
        {
            var result = await _dox.QueryAsync(SelectedOption, Query, ct);
            if (ct.IsCancellationRequested) return;

            if (!result.Ok)
            {
                ErrorMessage = result.Error ?? "Não foi possível consultar.";
                _notifications.ProcessFailed("23 DOX", ErrorMessage);
                return;
            }

            // Achatar a árvore também custa caro quando vem muita gente: fica fora da UI.
            var records = await Task.Run(() => BuildRecords(result.Groups, CopyValue), ct);
            if (ct.IsCancellationRequested) return;

            _records = records;
            _fieldCount = result.FieldCount;
            _recordCount = 0;
            var facts = new List<BlockFacts>();
            foreach (var r in records)
                if (!r.IsMeta) { _recordCount++; facts.Add(r.Facts); }

            IsTruncated = result.Truncated;
            _resultOption = SelectedOption;
            Filter.Reset(facts);
            ApplyFilter();
            HasResult = Blocks.Count > 0;
            OnPropertyChanged(nameof(CanFilter));

            if (!HasResult)
                ErrorMessage = "A consulta não retornou informações.";
            else
                _notifications.ProcessFinished("23 DOX", $"Consulta de {SelectedOption.Label} concluída.");
        }
        catch (OperationCanceledException)
        {
            // Cancelado pelo usuário (ou por uma consulta nova): nada a mostrar.
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsLoading = false;
        }
    }

    private void Cancelar()
    {
        if (!IsLoading) return;
        _cts?.Cancel();
        IsLoading = false;
        ErrorMessage = "Consulta cancelada.";
    }

    /// <summary>
    /// Cada bloco de topo vira um registro com sua árvore já achatada — é o que
    /// permite virtualizar a tela e, agora, filtrar registro por registro.
    /// </summary>
    private static List<DoxRecord> BuildRecords(IReadOnlyList<DoxGroup> groups, Action<string> onCopy)
    {
        var records = new List<DoxRecord>(groups.Count);
        foreach (var g in groups)
        {
            if (g.IsEmpty) continue;
            var blocks = new List<DoxBlockViewModel>();
            Append(blocks, g, 0, onCopy);
            records.Add(new DoxRecord(blocks, g.IsMeta));
        }
        return records;
    }

    private static void Append(List<DoxBlockViewModel> list, DoxGroup group, int depth, Action<string> onCopy)
    {
        if (group.IsEmpty) return;
        list.Add(new DoxBlockViewModel(group, depth, onCopy));
        foreach (var sub in group.SubGroups)
            Append(list, sub, depth + 1, onCopy);
    }

    /// <summary>
    /// Refaz a lista visível a partir dos critérios. Registro que não bate sai
    /// inteiro; dentro do que ficou, sub-bloco que contradiz o filtro (endereço de
    /// outro estado, telefone sem WhatsApp) some — bloco sem o dado é neutro.
    /// </summary>
    private void ApplyFilter()
    {
        var term = Filter.Term;
        var list = new List<DoxBlockViewModel>();
        var visible = 0;

        foreach (var record in _records)
        {
            if (record.IsMeta)
            {
                list.AddRange(record.Blocks);
                continue;
            }

            if (!Filter.KeepsRecord(record.Facts)) continue;
            if (term.Length > 0 && !record.ContainsText(term)) continue;

            visible++;
            foreach (var block in record.Blocks)
                if (Filter.KeepsBlock(block.Facts)) list.Add(block);
        }

        _visibleCount = visible;
        Blocks = list;
        OnPropertyChanged(nameof(IsEmptyByFilter));
    }

    /// <summary>
    /// Digitar dispara um filtro por tecla; um respiro curto evita refazer a lista
    /// no meio da palavra.
    /// </summary>
    private async void OnFilterChanged()
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        var cts = new CancellationTokenSource();
        _filterCts = cts;

        try { await Task.Delay(140, cts.Token); }
        catch (OperationCanceledException) { return; }

        if (!cts.IsCancellationRequested) ApplyFilter();
    }

    private void ClearResult()
    {
        HasResult = false;
        IsTruncated = false;
        Blocks = Array.Empty<DoxBlockViewModel>();
        _records = Array.Empty<DoxRecord>();
        _resultOption = null;
        _fieldCount = 0;
        _recordCount = 0;
        _visibleCount = 0;
        OnPropertyChanged(nameof(IsEmptyByFilter));
        OnPropertyChanged(nameof(CanFilter));
    }

    private async void CopyValue(string value)
    {
        if (await _clipboard.CopyAsync(value))
            _notifications.Show("Copiado", "Valor copiado para a área de transferência.");
    }

    private async void CopyAll()
    {
        if (_copying || Blocks.Count == 0) return;
        _copying = true;
        try
        {
            // Copia o que está na tela (já filtrado). Com muito resultado o texto tem
            // megabytes: montar fora da thread da interface.
            var blocks = Blocks;
            var text = await Task.Run(() => BuildPlainText(blocks));
            if (await _clipboard.CopyAsync(text))
                _notifications.Show("Copiado", "Todos os dados foram copiados em texto.");
        }
        finally
        {
            _copying = false;
        }
    }

    /// <summary>`RÓTULO: valor` linha a linha, na mesma ordem que está na tela.</summary>
    private static string BuildPlainText(IReadOnlyList<DoxBlockViewModel> blocks)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            if (block.HasTitle)
            {
                if (block.IsSection) sb.Append('\n').Append("── ").Append(block.Title).Append(" ──\n");
                else sb.Append('\n').Append('[').Append(block.Title).Append("]\n");
            }

            foreach (var field in block.Fields)
                sb.Append(field.Label).Append(": ").Append(field.Value).Append('\n');
        }
        return sb.ToString().Trim();
    }
}
