// ViewModels/HotViewModel.cs
using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Animus.Common;
using Animus.Models;
using Animus.Services;
using Avalonia.Threading;

namespace Animus.ViewModels;

public sealed class HotViewModel : ViewModelBase
{
    private const int MaxLogLines = 500;

    private readonly HotService _hot;
    private readonly FilePickerService _picker;
    private readonly AppDataStore _store;
    private readonly NotificationService _notifications;

    private readonly StringBuilder _log = new();
    private int _logLines;
    private CancellationTokenSource? _cts;

    private string _toolPath;
    private string _inputPath;
    private string _outputPath;
    private string _workersText;
    private string _timeoutText;
    private string _delayText;
    private string _maxDnsText;
    private string _dnsCacheTtlText;
    private string _maxConnsText;
    private string _poolSizeText;
    private string _proxyHealthText;
    private string _portScanTimeoutText;
    private string _portScanDialText;
    private string _portCacheTtlText;
    private string _httpMaxIdleText;
    private string _httpMaxHostText;
    private string _httpIdleTimeoutText;
    private string _subdomainsText;
    private string _userAgentsText;
    private string _languagesText;
    private bool _useProxies;
    private bool _stealth;
    private bool _pipeline;
    private bool _utls;
    private bool _insecure;
    private bool _doh;
    private bool _dot;
    private bool _enableIMAP;
    private bool _enablePOP3;
    private bool _enableSMTP;
    private bool _webmail;
    private bool _scrape2FA;
    private bool _http2;
    private bool _verbose;
    private bool _stats;

    private bool _isRunning;
    private string _logText = "";
    private string _summary = "";

    public HotViewModel(HotService hot, FilePickerService picker, AppDataStore store, NotificationService notifications)
    {
        _hot = hot;
        _picker = picker;
        _store = store;
        _notifications = notifications;

        var p = store.Data.Hot;
        _toolPath = p.ToolPath;
        _inputPath = p.InputPath;
        _outputPath = p.OutputPath;
        _workersText = p.Workers.ToString(CultureInfo.InvariantCulture);
        _timeoutText = p.Timeout;
        _delayText = p.Delay;
        _maxDnsText = p.MaxDns.ToString(CultureInfo.InvariantCulture);
        _dnsCacheTtlText = p.DnsCacheTtl;
        _maxConnsText = p.MaxConns.ToString(CultureInfo.InvariantCulture);
        _poolSizeText = p.PoolSize.ToString(CultureInfo.InvariantCulture);
        _proxyHealthText = p.ProxyHealth;
        _portScanTimeoutText = p.PortScanTimeout;
        _portScanDialText = p.PortScanDial;
        _portCacheTtlText = p.PortCacheTtl;
        _httpMaxIdleText = p.HttpMaxIdle.ToString(CultureInfo.InvariantCulture);
        _httpMaxHostText = p.HttpMaxHost.ToString(CultureInfo.InvariantCulture);
        _httpIdleTimeoutText = p.HttpIdleTimeout;
        _subdomainsText = p.Subdomains;
        _userAgentsText = p.UserAgents;
        _languagesText = p.Languages;
        _useProxies = p.UseProxies;
        _stealth = p.Stealth;
        _pipeline = p.Pipeline;
        _utls = p.Utls;
        _insecure = p.Insecure;
        _doh = p.Doh;
        _dot = p.Dot;
        _enableIMAP = p.EnableIMAP;
        _enablePOP3 = p.EnablePOP3;
        _enableSMTP = p.EnableSMTP;
        _webmail = p.Webmail;
        _scrape2FA = p.Scrape2FA;
        _http2 = p.Http2;
        _verbose = p.Verbose;
        _stats = p.Stats;

        PickInputCommand = new AsyncRelayCommand(PickInputAsync);
        PickOutputCommand = new AsyncRelayCommand(PickOutputAsync);
        RunCommand = new AsyncRelayCommand(RunAsync);
        CancelCommand = new RelayCommand(Cancel);
        ClearLogCommand = new RelayCommand(ClearLog);
    }

    public string ToolPath { get => _toolPath; set { if (SetProperty(ref _toolPath, value)) Persist(); } }
    public string InputPath { get => _inputPath; set { if (!SetProperty(ref _inputPath, value)) return; OnPropertyChanged(nameof(HasInput)); Persist(); } }
    public string OutputPath { get => _outputPath; set { if (!SetProperty(ref _outputPath, value)) return; OnPropertyChanged(nameof(HasOutput)); Persist(); } }
    public string WorkersText { get => _workersText; set { if (SetProperty(ref _workersText, value)) Persist(); } }
    public string TimeoutText { get => _timeoutText; set { if (SetProperty(ref _timeoutText, value)) Persist(); } }
    public string DelayText { get => _delayText; set { if (SetProperty(ref _delayText, value)) Persist(); } }
    public string MaxDnsText { get => _maxDnsText; set { if (SetProperty(ref _maxDnsText, value)) Persist(); } }
    public string DnsCacheTtlText { get => _dnsCacheTtlText; set { if (SetProperty(ref _dnsCacheTtlText, value)) Persist(); } }
    public string MaxConnsText { get => _maxConnsText; set { if (SetProperty(ref _maxConnsText, value)) Persist(); } }
    public string PoolSizeText { get => _poolSizeText; set { if (SetProperty(ref _poolSizeText, value)) Persist(); } }
    public string ProxyHealthText { get => _proxyHealthText; set { if (SetProperty(ref _proxyHealthText, value)) Persist(); } }
    public string PortScanTimeoutText { get => _portScanTimeoutText; set { if (SetProperty(ref _portScanTimeoutText, value)) Persist(); } }
    public string PortScanDialText { get => _portScanDialText; set { if (SetProperty(ref _portScanDialText, value)) Persist(); } }
    public string PortCacheTtlText { get => _portCacheTtlText; set { if (SetProperty(ref _portCacheTtlText, value)) Persist(); } }
    public string HttpMaxIdleText { get => _httpMaxIdleText; set { if (SetProperty(ref _httpMaxIdleText, value)) Persist(); } }
    public string HttpMaxHostText { get => _httpMaxHostText; set { if (SetProperty(ref _httpMaxHostText, value)) Persist(); } }
    public string HttpIdleTimeoutText { get => _httpIdleTimeoutText; set { if (SetProperty(ref _httpIdleTimeoutText, value)) Persist(); } }
    public string SubdomainsText { get => _subdomainsText; set { if (SetProperty(ref _subdomainsText, value)) Persist(); } }
    public string UserAgentsText { get => _userAgentsText; set { if (SetProperty(ref _userAgentsText, value)) Persist(); } }
    public string LanguagesText { get => _languagesText; set { if (SetProperty(ref _languagesText, value)) Persist(); } }
    public bool UseProxies { get => _useProxies; set { if (SetProperty(ref _useProxies, value)) Persist(); } }
    public bool Stealth { get => _stealth; set { if (SetProperty(ref _stealth, value)) Persist(); } }
    public bool Pipeline { get => _pipeline; set { if (SetProperty(ref _pipeline, value)) Persist(); } }
    public bool Utls { get => _utls; set { if (SetProperty(ref _utls, value)) Persist(); } }
    public bool Insecure { get => _insecure; set { if (SetProperty(ref _insecure, value)) Persist(); } }
    public bool Doh { get => _doh; set { if (SetProperty(ref _doh, value)) Persist(); } }
    public bool Dot { get => _dot; set { if (SetProperty(ref _dot, value)) Persist(); } }
    public bool EnableIMAP { get => _enableIMAP; set { if (SetProperty(ref _enableIMAP, value)) Persist(); } }
    public bool EnablePOP3 { get => _enablePOP3; set { if (SetProperty(ref _enablePOP3, value)) Persist(); } }
    public bool EnableSMTP { get => _enableSMTP; set { if (SetProperty(ref _enableSMTP, value)) Persist(); } }
    public bool Webmail { get => _webmail; set { if (SetProperty(ref _webmail, value)) Persist(); } }
    public bool Scrape2FA { get => _scrape2FA; set { if (SetProperty(ref _scrape2FA, value)) Persist(); } }
    public bool Http2 { get => _http2; set { if (SetProperty(ref _http2, value)) Persist(); } }
    public bool Verbose { get => _verbose; set { if (SetProperty(ref _verbose, value)) Persist(); } }
    public bool Stats { get => _stats; set { if (SetProperty(ref _stats, value)) Persist(); } }

    public bool IsRunning { get => _isRunning; private set { if (!SetProperty(ref _isRunning, value)) return; OnPropertyChanged(nameof(IsIdle)); } }
    public bool IsIdle => !IsRunning;
    public bool HasInput => !string.IsNullOrWhiteSpace(InputPath);
    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputPath);

    public string LogText { get => _logText; private set { if (SetProperty(ref _logText, value)) OnPropertyChanged(nameof(HasLog)); } }
    public bool HasLog => _logText.Length > 0;

    public string Summary { get => _summary; private set { if (SetProperty(ref _summary, value)) OnPropertyChanged(nameof(HasSummary)); } }
    public bool HasSummary => _summary.Length > 0;

    public ICommand PickInputCommand { get; }
    public ICommand PickOutputCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearLogCommand { get; }

    private async Task PickInputAsync()
    {
        var path = await _picker.PickOpenAsync("Escolha o arquivo de contas");
        if (!string.IsNullOrEmpty(path)) InputPath = path;
    }

    private async Task PickOutputAsync()
    {
        var path = await _picker.PickSaveAsync("Salvar relatório do checker", "chk-resultado.jsonl");
        if (!string.IsNullOrEmpty(path)) OutputPath = path;
    }

    private async Task RunAsync()
    {
        if (IsRunning) return;

        var options = new HotRunOptions
        {
            ToolPath = ToolPath.Trim(),
            InputPath = InputPath.Trim(),
            OutputPath = OutputPath.Trim(),
            Workers = ParseInt(WorkersText, 50, 1, 500),
            Timeout = TimeoutText.Trim(),
            Delay = DelayText.Trim(),
            MaxDns = ParseInt(MaxDnsText, 20, 5, 100),
            DnsCacheTtl = DnsCacheTtlText.Trim(),
            MaxConns = ParseInt(MaxConnsText, 100, 10, 1000),
            PoolSize = ParseInt(PoolSizeText, 3, 1, 10),
            ProxyHealth = ProxyHealthText.Trim(),
            PortScanTimeout = PortScanTimeoutText.Trim(),
            PortScanDial = PortScanDialText.Trim(),
            PortCacheTtl = PortCacheTtlText.Trim(),
            HttpMaxIdle = ParseInt(HttpMaxIdleText, 100, 10, 500),
            HttpMaxHost = ParseInt(HttpMaxHostText, 20, 5, 100),
            HttpIdleTimeout = HttpIdleTimeoutText.Trim(),
            Subdomains = SubdomainsText,
            UserAgents = UserAgentsText,
            Languages = LanguagesText,
            UseProxies = UseProxies,
            Stealth = Stealth,
            Pipeline = Pipeline,
            Utls = Utls,
            Insecure = Insecure,
            Doh = Doh,
            Dot = Dot,
            EnableIMAP = EnableIMAP,
            EnablePOP3 = EnablePOP3,
            EnableSMTP = EnableSMTP,
            Webmail = Webmail,
            Scrape2FA = Scrape2FA,
            Http2 = Http2,
            Verbose = Verbose,
            Stats = Stats,
        };
        Persist();

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        ClearLog();
        Summary = "";
        IsRunning = true;
        AppendLine($"▶ iniciando checker — entrada: {options.InputPath}");

        HotRunResult result;
        try
        {
            result = await _hot.RunAsync(options, AppendLine, _cts.Token);
        }
        catch (Exception ex)
        {
            result = new HotRunResult { Error = ex.Message };
        }
        finally
        {
            IsRunning = false;
        }

        if (result.Error is not null)
        {
            AppendLine("✖ " + result.Error);
            Summary = result.Error;
            _notifications.ProcessFailed("23 HOT", result.Error);
            return;
        }

        if (_cts.IsCancellationRequested)
        {
            AppendLine("■ cancelado.");
            Summary = "Cancelado.";
            return;
        }

        if (result.ExitCode != 0)
        {
            var msg = $"Checker encerrou com erro (exit code {result.ExitCode}).";
            AppendLine("✖ " + msg);
            Summary = msg;
            _notifications.ProcessFailed("23 HOT", msg);
            return;
        }

        Summary = $"Processado: {result.Total} | Live: {result.Ok} | 2FA: {result.Twofa} | Erros: {result.Errors}";
        AppendLine($"✔ relatório salvo em: {options.OutputPath}");
        _notifications.ProcessFinished("23 HOT", $"Checker concluído: {result.Ok} live.");
    }

    private void Cancel()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        AppendLine("■ cancelando…");
    }

    private void ClearLog()
    {
        _log.Clear();
        _logLines = 0;
        LogText = "";
    }

    private void AppendLine(string line)
    {
        if (Dispatcher.UIThread.CheckAccess()) AppendCore(line);
        else Dispatcher.UIThread.Post(() => AppendCore(line));
    }

    private void AppendCore(string line)
    {
        if (_logLines >= MaxLogLines)
        {
            var text = _log.ToString();
            var keepFrom = text.IndexOf('\n', text.Length / 2) + 1;
            _log.Clear();
            _log.Append(text, keepFrom, text.Length - keepFrom);
            _logLines = CountLines(_log);
        }
        _log.Append(line).Append('\n');
        _logLines++;
        LogText = _log.ToString();
    }

    private static int CountLines(StringBuilder sb)
    {
        var n = 0;
        for (var i = 0; i < sb.Length; i++)
            if (sb[i] == '\n') n++;
        return n;
    }

    private static int ParseInt(string text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return fallback;
        return Math.Clamp(n, min, max);
    }

    private void Persist()
    {
        _store.Data.Hot = new HotPrefs
        {
            ToolPath = ToolPath,
            InputPath = InputPath,
            OutputPath = OutputPath,
            Workers = ParseInt(WorkersText, 50, 1, 500),
            Timeout = TimeoutText,
            Delay = DelayText,
            MaxDns = ParseInt(MaxDnsText, 20, 5, 100),
            DnsCacheTtl = DnsCacheTtlText,
            MaxConns = ParseInt(MaxConnsText, 100, 10, 1000),
            PoolSize = ParseInt(PoolSizeText, 3, 1, 10),
            ProxyHealth = ProxyHealthText,
            PortScanTimeout = PortScanTimeoutText,
            PortScanDial = PortScanDialText,
            PortCacheTtl = PortCacheTtlText,
            HttpMaxIdle = ParseInt(HttpMaxIdleText, 100, 10, 500),
            HttpMaxHost = ParseInt(HttpMaxHostText, 20, 5, 100),
            HttpIdleTimeout = HttpIdleTimeoutText,
            Subdomains = SubdomainsText,
            UserAgents = UserAgentsText,
            Languages = LanguagesText,
            UseProxies = UseProxies,
            Stealth = Stealth,
            Pipeline = Pipeline,
            Utls = Utls,
            Insecure = Insecure,
            Doh = Doh,
            Dot = Dot,
            EnableIMAP = EnableIMAP,
            EnablePOP3 = EnablePOP3,
            EnableSMTP = EnableSMTP,
            Webmail = Webmail,
            Scrape2FA = Scrape2FA,
            Http2 = Http2,
            Verbose = Verbose,
            Stats = Stats,
        };
        _store.Save();
    }
}