// Services/HotService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Animus.Services;

public sealed class HotRunOptions
{
    public string ToolPath = "";
    public string InputPath = "";
    public string OutputPath = "";
    public int Workers = 50;
    public string Timeout = "8s";
    public string Delay = "0";
    public int MaxDns = 20;
    public string DnsCacheTtl = "5m";
    public int MaxConns = 100;
    public int PoolSize = 3;
    public string ProxyHealth = "30s";
    public string PortScanTimeout = "3s";
    public string PortScanDial = "1s";
    public string PortCacheTtl = "5m";
    public int HttpMaxIdle = 100;
    public int HttpMaxHost = 20;
    public string HttpIdleTimeout = "90s";
    public string Subdomains = "";
    public string UserAgents = "";
    public string Languages = "";
    public bool UseProxies;
    public bool Stealth;
    public bool Pipeline;
    public bool Utls;
    public bool Insecure = true;
    public bool Doh;
    public bool Dot;
    public bool EnableIMAP = true;
    public bool EnablePOP3 = true;
    public bool EnableSMTP = true;
    public bool Webmail = true;
    public bool Scrape2FA = true;
    public bool Http2;
    public bool Verbose;
    public bool Stats;
}

public sealed class HotRunResult
{
    public bool Started;
    public int ExitCode;
    public int Total, Ok, Twofa, Errors;
    public string? Error;
}

public sealed class HotService
{
    public async Task<HotRunResult> RunAsync(HotRunOptions o, Action<string> onLine, CancellationToken ct)
    {
        var result = new HotRunResult();

        if (string.IsNullOrWhiteSpace(o.InputPath) || !File.Exists(o.InputPath))
        {
            result.Error = "Escolha um arquivo de entrada válido (a lista de contas).";
            return result;
        }
        if (string.IsNullOrWhiteSpace(o.OutputPath))
        {
            result.Error = "Escolha onde salvar o relatório.";
            return result;
        }

        var (fileName, prefix, err) = ResolveTool(o.ToolPath);
        if (err is not null) { result.Error = err; return result; }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in prefix) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(o.Workers.ToString());
        psi.ArgumentList.Add("-timeout"); psi.ArgumentList.Add(o.Timeout);
        psi.ArgumentList.Add("-delay"); psi.ArgumentList.Add(o.Delay);
        psi.ArgumentList.Add("-max-dns"); psi.ArgumentList.Add(o.MaxDns.ToString());
        psi.ArgumentList.Add("-dns-cache-ttl"); psi.ArgumentList.Add(o.DnsCacheTtl);
        psi.ArgumentList.Add("-max-conns"); psi.ArgumentList.Add(o.MaxConns.ToString());
        psi.ArgumentList.Add("-pool-size"); psi.ArgumentList.Add(o.PoolSize.ToString());
        psi.ArgumentList.Add("-proxy-check"); psi.ArgumentList.Add(o.ProxyHealth);
        psi.ArgumentList.Add("-port-scan-timeout"); psi.ArgumentList.Add(o.PortScanTimeout);
        psi.ArgumentList.Add("-port-scan-dial"); psi.ArgumentList.Add(o.PortScanDial);
        psi.ArgumentList.Add("-port-cache-ttl"); psi.ArgumentList.Add(o.PortCacheTtl);
        psi.ArgumentList.Add("-http-max-idle"); psi.ArgumentList.Add(o.HttpMaxIdle.ToString());
        psi.ArgumentList.Add("-http-max-host"); psi.ArgumentList.Add(o.HttpMaxHost.ToString());
        psi.ArgumentList.Add("-http-idle-timeout"); psi.ArgumentList.Add(o.HttpIdleTimeout);
        if (!string.IsNullOrWhiteSpace(o.Subdomains))
        {
            psi.ArgumentList.Add("-subs"); psi.ArgumentList.Add(string.Join(",", o.Subdomains.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)));
        }
        if (!string.IsNullOrWhiteSpace(o.UserAgents))
        {
            psi.ArgumentList.Add("-user-agents"); psi.ArgumentList.Add(string.Join(",", o.UserAgents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)));
        }
        if (!string.IsNullOrWhiteSpace(o.Languages))
        {
            psi.ArgumentList.Add("-languages"); psi.ArgumentList.Add(string.Join(",", o.Languages.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)));
        }
        if (o.UseProxies) { psi.ArgumentList.Add("-proxies"); psi.ArgumentList.Add("proxies.txt"); }
        if (o.Stealth) psi.ArgumentList.Add("-stealth");
        if (o.Pipeline) psi.ArgumentList.Add("-pipeline");
        if (o.Utls) psi.ArgumentList.Add("-utls");
        if (o.Insecure) psi.ArgumentList.Add("-insecure");
        if (o.Doh) psi.ArgumentList.Add("-doh");
        if (o.Dot) psi.ArgumentList.Add("-dot");
        if (!o.EnableIMAP) psi.ArgumentList.Add("-imap=false");
        if (!o.EnablePOP3) psi.ArgumentList.Add("-pop3=false");
        if (!o.EnableSMTP) psi.ArgumentList.Add("-smtp=false");
        if (!o.Webmail) psi.ArgumentList.Add("-webmail=false");
        if (!o.Scrape2FA) psi.ArgumentList.Add("-scrape-2fa=false");
        if (o.Http2) psi.ArgumentList.Add("-http2");
        if (o.Verbose) psi.ArgumentList.Add("-v");
        if (o.Stats) psi.ArgumentList.Add("-stats");

        var livePath = o.OutputPath;
        var twofaDir = Path.GetDirectoryName(o.OutputPath) ?? ".";
        var twofaName = Path.GetFileNameWithoutExtension(o.OutputPath) + "_2fa" + Path.GetExtension(o.OutputPath);
        var twofaPath = Path.Combine(twofaDir, twofaName);
        psi.ArgumentList.Add("-live"); psi.ArgumentList.Add(livePath);
        psi.ArgumentList.Add("-twofa"); psi.ArgumentList.Add(twofaPath);

        psi.ArgumentList.Add(o.InputPath);

        using var proc = new Process { StartInfo = psi };
        try
        {
            if (!proc.Start()) { result.Error = "Não consegui iniciar o checker."; return result; }
        }
        catch (Exception ex)
        {
            result.Error = "Falha ao iniciar o checker: " + ex.Message;
            return result;
        }
        result.Started = true;

        var outTask = PumpAsync(proc.StandardOutput, line => HandleLine(line, result, onLine));
        var errTask = PumpAsync(proc.StandardError, onLine);

        await using (ct.Register(() => TryKill(proc)))
        {
            await Task.WhenAll(outTask, errTask);
            await proc.WaitForExitAsync(CancellationToken.None);
        }

        result.ExitCode = proc.ExitCode;
        return result;
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
            onLine(line);
    }

    private static void HandleLine(string line, HotRunResult result, Action<string> onLine)
    {
        if (line.StartsWith("Processed: ", StringComparison.Ordinal))
        {
            try
            {
                var parts = line.Split('|');
                foreach (var p in parts)
                {
                    var kv = p.Trim().Split(':');
                    if (kv.Length != 2) continue;
                    var key = kv[0].Trim();
                    var val = int.Parse(kv[1].Trim());
                    switch (key)
                    {
                        case "Processed": result.Total = val; break;
                        case "Live": result.Ok = val; break;
                        case "2FA": result.Twofa = val; break;
                        case "Errors": result.Errors = val; break;
                    }
                }
            }
            catch { }
            return;
        }
        onLine(line);
    }

    private static void TryKill(Process proc)
    {
        try { if (!proc.HasExited) proc.Kill(true); }
        catch { }
    }

    private static (string fileName, List<string> prefix, string? error) ResolveTool(string configured)
    {
        var tool = string.IsNullOrWhiteSpace(configured) ? Locate() : configured.Trim();
        if (tool is null)
            return ("", new(), "Não encontrei o checker. Aponte o caminho no campo 'Checker (Go)'.");
        if (!File.Exists(tool) && !Directory.Exists(tool))
            return ("", new(), $"O caminho do checker não existe: {tool}");

        if (Directory.Exists(tool) || tool.EndsWith(".go", StringComparison.OrdinalIgnoreCase))
        {
            var go = FindOnPath(OperatingSystem.IsWindows() ? "go.exe" : "go");
            if (go is null)
                return ("", new(), "Para rodar o código-fonte preciso do Go instalado (`go`). Ou aponte para o binário já compilado.");
            return (go, new List<string> { "run", tool }, null);
        }

        return (tool, new List<string>(), null);
    }

    private static string? Locate()
    {
        var exe = OperatingSystem.IsWindows() ? "chk.exe" : "chk";
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(root);
            for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
            {
                var bin = Path.Combine(dir.FullName, "Tools", "chk", exe);
                if (File.Exists(bin)) return bin;
                var src = Path.Combine(dir.FullName, "Tools", "chk", "chk.go");
                if (File.Exists(src)) return src;
            }
        }
        return null;
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }
}