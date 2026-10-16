namespace DrawingFlow.Server.Infrastructure;

/// <summary>Работа за обратным прокси (nginx, IIS ARR): доверие заголовкам X-Forwarded-For/-Proto только от перечисленных адресов.</summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public bool Enabled { get; set; }

    /// <summary>IP-адреса прокси, например ["10.0.0.5"]. Пустые списки означают доверие любому отправителю — допустимо только если сервер недоступен напрямую.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Сети прокси в нотации CIDR, например ["10.0.0.0/8"].</summary>
    public string[] KnownNetworks { get; set; } = [];
}
