using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using Animus.Common;
using Animus.Services;

namespace Animus.ViewModels;

/// <summary>Uma opção de combo do filtro. <c>Value</c> nulo = "todos".</summary>
public sealed record DoxFilterOption(string Label, string? Value);

/// <summary>
/// O que dá pra filtrar num bloco: estado, idade, gênero e WhatsApp. Sai dos
/// próprios campos do bloco — a UF vem da cidade ("ARAGUAINA-TO"), e quando não
/// dá, do CEP ou do DDD.
/// </summary>
public sealed class BlockFacts
{
    public HashSet<string>? Ufs { get; private set; }
    public int? Idade { get; private set; }
    public char? Genero { get; private set; }
    public bool? Whatsapp { get; private set; }

    /// <summary>Valores do bloco em minúsculas, para a busca livre.</summary>
    public string Text { get; private set; } = "";

    public bool IsEmpty => Ufs is null && Idade is null && Genero is null && Whatsapp is null;

    public static BlockFacts FromFields(IReadOnlyList<DoxField> fields)
    {
        var facts = new BlockFacts();
        if (fields.Count == 0) return facts;

        var text = new StringBuilder();
        foreach (var f in fields)
        {
            text.Append(f.Value.ToLowerInvariant()).Append('\n');

            var label = f.Label;
            if (IsUfLabel(label)) facts.AddUf(BrazilRegions.Normalize(f.Value));
            else if (IsCityLabel(label)) facts.AddUf(BrazilRegions.FromCity(f.Value));
            else if (IsCepLabel(label)) facts.AddUf(BrazilRegions.FromCep(f.Value));
            else if (IsDddLabel(label)) facts.AddUf(BrazilRegions.FromDdd(f.Value));
            else if (IsAgeLabel(label)) facts.Idade ??= ParseAge(f.Value);
            else if (IsBirthLabel(label)) facts.Idade ??= AgeFromBirth(f.Value);
            else if (IsGenderLabel(label)) facts.Genero ??= ParseGender(f.Value);
            else if (IsWhatsappLabel(label)) facts.Whatsapp ??= ParseBool(f.Value);
        }

        facts.Text = text.ToString();
        return facts;
    }

    /// <summary>Junta os fatos dos filhos: o registro inteiro vale pelo que tem em qualquer nível.</summary>
    public void Merge(BlockFacts other)
    {
        if (other.Ufs is not null) foreach (var uf in other.Ufs) AddUf(uf);
        Idade ??= other.Idade;
        Genero ??= other.Genero;
        if (other.Whatsapp == true) Whatsapp = true;
        else Whatsapp ??= other.Whatsapp;
    }

    private void AddUf(string? uf)
    {
        if (uf is null) return;
        (Ufs ??= new(StringComparer.Ordinal)).Add(uf);
    }

    // ---- leitura dos rótulos (eles saem das chaves do JSON, sempre em maiúsculas) ----
    private static bool IsUfLabel(string l) => l is "UF" or "ESTADO" || l.EndsWith(" UF", StringComparison.Ordinal);
    private static bool IsCepLabel(string l) => l is "CEP" || l.EndsWith(" CEP", StringComparison.Ordinal);
    private static bool IsDddLabel(string l) => l is "DDD";
    private static bool IsCityLabel(string l) => l is "CIDADE" or "MUNICIPIO" or "MUNICÍPIO";
    private static bool IsAgeLabel(string l) => l is "IDADE";
    private static bool IsBirthLabel(string l) => l.Contains("NASCIMENTO", StringComparison.Ordinal);
    private static bool IsGenderLabel(string l) => l is "GÊNERO" or "GENERO" or "SEXO";
    private static bool IsWhatsappLabel(string l) => l.Contains("WHATSAPP", StringComparison.Ordinal);

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "dd/MM/yyyy", "yyyyMMdd", "dd-MM-yyyy", "MM/dd/yyyy",
    };

    private static int? AgeFromBirth(string value)
    {
        var v = value.Trim();
        if (v.Length > 10) v = v[..10];

        if (!DateTime.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var birth)
            && !DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out birth))
            return null;

        var today = DateTime.Today;
        var age = today.Year - birth.Year;
        if (birth.Date > today.AddYears(-age)) age--;
        return age is >= 0 and <= 120 ? age : null;
    }

    private static int? ParseAge(string value)
        => int.TryParse(value.Trim(), out var age) && age is >= 0 and <= 120 ? age : null;

    private static char? ParseGender(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return null;
        return char.ToUpperInvariant(v[0]) switch { 'M' => 'M', 'F' => 'F', _ => null };
    }

    private static bool? ParseBool(string value) => value.Trim().ToUpperInvariant() switch
    {
        "SIM" or "TRUE" or "1" or "S" => true,
        "NÃO" or "NAO" or "FALSE" or "0" or "N" => false,
        _ => null,
    };
}

/// <summary>
/// O menu de filtros do resultado do 23 DOX. Cada filtro só aparece se o
/// resultado tiver aquele dado — puxada por nome mostra estado/idade/gênero,
/// puxada por telefone mostra estado/WhatsApp, e assim por diante.
/// </summary>
public sealed class DoxFilterViewModel : ViewModelBase
{
    private static readonly DoxFilterOption AllUfs = new("Todos os estados", null);

    private DoxFilterOption _uf = AllUfs;
    private string _minAge = "";
    private string _maxAge = "";
    private string _text = "";
    private char? _genero;
    private bool _onlyWhatsapp;

    private bool _hasUf, _hasAge, _hasGender, _hasWhatsapp;

    public DoxFilterViewModel()
    {
        UfOptions = new ObservableCollection<DoxFilterOption> { AllUfs };
        ClearCommand = new RelayCommand(Clear);
        SetGenderCommand = new RelayCommand(p => Genero = (p as string) switch
        {
            "M" => 'M',
            "F" => 'F',
            _ => null,
        });
        ToggleWhatsappCommand = new RelayCommand(() => OnlyWhatsapp = !OnlyWhatsapp);
    }

    /// <summary>Avisa a página que os critérios mudaram (ela reaplica o filtro).</summary>
    public event Action? Changed;

    public ObservableCollection<DoxFilterOption> UfOptions { get; }

    public ICommand ClearCommand { get; }
    public ICommand SetGenderCommand { get; }
    public ICommand ToggleWhatsappCommand { get; }

    public DoxFilterOption SelectedUf
    {
        get => _uf;
        set { if (SetProperty(ref _uf, value ?? AllUfs)) Touch(); }
    }

    public string MinAge
    {
        get => _minAge;
        set { if (SetProperty(ref _minAge, value)) Touch(); }
    }

    public string MaxAge
    {
        get => _maxAge;
        set { if (SetProperty(ref _maxAge, value)) Touch(); }
    }

    /// <summary>Busca livre em qualquer valor do resultado.</summary>
    public string Text
    {
        get => _text;
        set { if (SetProperty(ref _text, value)) Touch(); }
    }

    public char? Genero
    {
        get => _genero;
        private set
        {
            if (!SetProperty(ref _genero, value)) return;
            OnPropertyChanged(nameof(IsAnyGender));
            OnPropertyChanged(nameof(IsMale));
            OnPropertyChanged(nameof(IsFemale));
            Touch();
        }
    }

    public bool IsAnyGender => _genero is null;
    public bool IsMale => _genero == 'M';
    public bool IsFemale => _genero == 'F';

    public bool OnlyWhatsapp
    {
        get => _onlyWhatsapp;
        private set { if (SetProperty(ref _onlyWhatsapp, value)) Touch(); }
    }

    public bool HasUf { get => _hasUf; private set => SetProperty(ref _hasUf, value); }
    public bool HasAge { get => _hasAge; private set => SetProperty(ref _hasAge, value); }
    public bool HasGender { get => _hasGender; private set => SetProperty(ref _hasGender, value); }
    public bool HasWhatsapp { get => _hasWhatsapp; private set => SetProperty(ref _hasWhatsapp, value); }

    /// <summary>Se nada é filtrável, o botão de filtros não aparece.</summary>
    public bool IsAvailable => HasUf || HasAge || HasGender || HasWhatsapp;

    /// <summary>Algum critério escolhido.</summary>
    public bool IsActive => _uf.Value is not null
                            || _genero is not null || _onlyWhatsapp
                            || MinAgeValue is not null || MaxAgeValue is not null
                            || Term.Length > 0;

    private int? MinAgeValue => int.TryParse(_minAge.Trim(), out var v) ? v : null;
    private int? MaxAgeValue => int.TryParse(_maxAge.Trim(), out var v) ? v : null;

    /// <summary>O texto da busca livre, já normalizado (vazio = sem busca).</summary>
    public string Term => _text.Trim().ToLowerInvariant();

    /// <summary>Prepara o menu para um resultado novo: monta as opções e zera os critérios.</summary>
    public void Reset(IReadOnlyList<BlockFacts> records)
    {
        var ufs = new SortedSet<string>(StringComparer.Ordinal);
        bool age = false, gender = false, whats = false;

        foreach (var r in records)
        {
            if (r.Ufs is not null) ufs.UnionWith(r.Ufs);
            age |= r.Idade is not null;
            gender |= r.Genero is not null;
            whats |= r.Whatsapp is not null;
        }

        UfOptions.Clear();
        UfOptions.Add(AllUfs);
        foreach (var uf in ufs) UfOptions.Add(new DoxFilterOption(uf, uf));

        // Um estado só não dá o que filtrar.
        HasUf = ufs.Count > 1;
        HasAge = age;
        HasGender = gender;
        HasWhatsapp = whats;
        OnPropertyChanged(nameof(IsAvailable));

        ClearQuiet();
    }

    private void Clear()
    {
        if (!IsActive) return;
        ClearQuiet();
        Changed?.Invoke();
    }

    private void ClearQuiet()
    {
        _uf = AllUfs;
        _minAge = "";
        _maxAge = "";
        _text = "";
        _genero = null;
        _onlyWhatsapp = false;

        OnPropertyChanged(nameof(SelectedUf));
        OnPropertyChanged(nameof(MinAge));
        OnPropertyChanged(nameof(MaxAge));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsAnyGender));
        OnPropertyChanged(nameof(IsMale));
        OnPropertyChanged(nameof(IsFemale));
        OnPropertyChanged(nameof(OnlyWhatsapp));
        OnPropertyChanged(nameof(IsActive));
    }

    private void Touch()
    {
        OnPropertyChanged(nameof(IsActive));
        Changed?.Invoke();
    }

    /// <summary>O registro inteiro passa? (o texto livre vale pelo registro todo)</summary>
    public bool KeepsRecord(BlockFacts aggregate) => !Contradicts(aggregate);

    /// <summary>Dentro de um registro que passou, some o bloco que contradiz o filtro.</summary>
    public bool KeepsBlock(BlockFacts facts) => !Contradicts(facts);

    /// <summary>Bloco sem o dado é neutro: não é excluído por um filtro que não o alcança.</summary>
    private bool Contradicts(BlockFacts f)
    {
        if (_uf.Value is { } uf && f.Ufs is { Count: > 0 } && !f.Ufs.Contains(uf)) return true;
        if (_genero is { } g && f.Genero is { } fg && fg != g) return true;
        if (_onlyWhatsapp && f.Whatsapp == false) return true;

        if (f.Idade is { } idade)
        {
            if (MinAgeValue is { } min && idade < min) return true;
            if (MaxAgeValue is { } max && idade > max) return true;
        }

        return false;
    }
}
