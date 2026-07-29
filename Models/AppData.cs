using System;
using System.Collections.Generic;

namespace Animus.Models;

/// <summary>Estado persistido em disco (senhas e preferencias).</summary>
public sealed class AppData
{
    public Dictionary<string, StoredUser> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nome do arquivo de fundo escolhido, ex.: "bg2.png". O fundo e unico (nao faz parte de tema).</summary>
    public string? Background { get; set; }

    public AppearanceData Appearance { get; set; } = new();

    public NotificationPrefs Notifications { get; set; } = new();

    public HotPrefs Hot { get; set; } = new();
}

/// <summary>
/// Escolhas do 23 HOT (o verificador IMAP em Go). Ficam salvas no config.json
/// para o app lembrar qual arquivo ler, onde salvar e com quais opções rodar.
/// </summary>
// Models/AppData.cs (HotPrefs)
public sealed class HotPrefs
{
    public string ToolPath { get; set; } = "";
    public string InputPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public int Workers { get; set; } = 50;
    public string Timeout { get; set; } = "8s";
    public string Delay { get; set; } = "0";
    public int MaxDns { get; set; } = 20;
    public string DnsCacheTtl { get; set; } = "5m";
    public int MaxConns { get; set; } = 100;
    public int PoolSize { get; set; } = 3;
    public string ProxyHealth { get; set; } = "30s";
    public string PortScanTimeout { get; set; } = "3s";
    public string PortScanDial { get; set; } = "1s";
    public string PortCacheTtl { get; set; } = "5m";
    public int HttpMaxIdle { get; set; } = 100;
    public int HttpMaxHost { get; set; } = 20;
    public string HttpIdleTimeout { get; set; } = "90s";
    public string Subdomains { get; set; } = "";
    public string UserAgents { get; set; } = "";
    public string Languages { get; set; } = "";
    public bool UseProxies { get; set; }
    public bool Stealth { get; set; }
    public bool Pipeline { get; set; }
    public bool Utls { get; set; }
    public bool Insecure { get; set; } = true;
    public bool Doh { get; set; }
    public bool Dot { get; set; }
    public bool EnableIMAP { get; set; } = true;
    public bool EnablePOP3 { get; set; } = true;
    public bool EnableSMTP { get; set; } = true;
    public bool Webmail { get; set; } = true;
    public bool Scrape2FA { get; set; } = true;
    public bool Http2 { get; set; }
    public bool Verbose { get; set; }
    public bool Stats { get; set; }
}
public sealed class StoredUser
{
    public string DisplayName { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
}

/// <summary>Tudo que o usuario pode personalizar na aparencia. Valores continuos = sliders.</summary>
public sealed class AppearanceData
{
    public string Accent { get; set; } = "#2ee27a";
    public string FontFamily { get; set; } = "Sora";

    /// <summary>Escala do texto: 0.85 a 1.25.</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>Opacidade dos paineis: 0.30 (bem transparente) a 1.0 (solido).</summary>
    public double PanelOpacity { get; set; } = 0.78;

    /// <summary>Escurecimento sobre o fundo: 0.45 (fundo bem visivel) a 1.0 (fundo sumindo).</summary>
    public double BackgroundDim { get; set; } = 0.50;

    /// <summary>Arredondamento das caixas: 0 a 24.</summary>
    public double CornerRadius { get; set; } = 13;

    /// <summary>Espacamento interno dos cards: 14 a 36.</summary>
    public double CardPadding { get; set; } = 24;

    /// <summary>Tamanho das abas e botoes de opcao: 0.85 a 1.25.</summary>
    public double ControlScale { get; set; } = 1.0;

    public bool Shadows { get; set; } = true;
    public bool Animations { get; set; } = true;
}
public sealed class NotificationPrefs
{
    public bool OnProcessFinished { get; set; } = true;
    public bool OnProcessFailed { get; set; } = true;
    public bool OnSettingsSaved { get; set; } = true;
}
