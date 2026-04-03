using CloudPub.Components;
using CloudPub.Options;
using Protocol;

namespace CloudPub;

public sealed class CloudPubException(string message, Exception? inner = null)
    : Exception(message, inner);

public static class TypesExtensions
{
    public static string ToWireName(this ProtocolType protocol) => protocol switch
    {
        ProtocolType.Http => "http",
        ProtocolType.Https => "https",
        ProtocolType.Tcp => "tcp",
        ProtocolType.Udp => "udp",
        ProtocolType.OneC => "1c",
        ProtocolType.Minecraft => "minecraft",
        ProtocolType.Webdav => "webdav",
        ProtocolType.Rtsp => "rtsp",
        ProtocolType.Rdp => "rdp",
        ProtocolType.Vnc => "vnc",
        ProtocolType.Ssh => "ssh",
        _ => "unknown"
    };

    public static uint? DefaultPort(this ProtocolType protocol) => protocol switch
    {
        ProtocolType.Http => 80,
        ProtocolType.Https => 443,
        ProtocolType.Minecraft => 25565,
        ProtocolType.Rtsp => 554,
        ProtocolType.Rdp => 3389,
        ProtocolType.Vnc => 5900,
        ProtocolType.Ssh => 22,
        _ => null
    };

    public static Uri ToEndpointUri(this ServerEndpoint ep)
    {
        ProtocolType proto = ep.RemoteProto;
        string wire = proto.ToWireName();
        return new Uri($"{wire}://{ep.RemoteAddr}:{ep.RemotePort}");
    }

    public static Endpoint ToEndpoint(this ServerEndpoint ep)
    {
        return new Endpoint
        {
            Guid = ep.Guid,
            Url = ep.ToEndpointUri().ToString(),
            Name = ep.Client?.Description,
            Status = ep.HasStatus ? ep.Status : null,
            Protocol = ep.RemoteProto
        };
    }

    public static string ToHostAndPort(this Uri url)
        => $"{GetHost(url)}:{GetPort(url)}";

    private static string GetHost(Uri serverUri) => serverUri.Host.TrimStart('[').TrimEnd(']');
    private static int GetPort(Uri serverUri) => serverUri.IsDefaultPort ? (serverUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? 80 : 443) : serverUri.Port;
}

public static class MessageExchangerExtensions
{
    public static async ValueTask<Message> WaitMessageOfType(this IMessageExchanger exchanger, CancellationToken cancellationToken, params Message.MessageOneofCase[] types)
    {
        while (true)
        {
            await exchanger.WaitForMessagesAsync();
            Message? message = await exchanger.ReadMessageOfType(cancellationToken, types);

            if (message != null)
                return message;
        }
    }

    public static async ValueTask<Message?> ReadMessageOfType(this IMessageExchanger exchanger, CancellationToken cancellationToken, params Message.MessageOneofCase[] types)
    {
        return await exchanger.ReadMessagesAsync().FirstOrDefaultAsync(msg => types.Contains(msg.MessageCase), cancellationToken);
    }
}

public static class CloudPubClientOptionsExtensions
{
    public static ClientEndpoint CreateCleintEndpoint(this CloudPubPublishOptions options)
    {
        AuthType resolvedAuth = options.Auth ?? (options.Protocol == ProtocolType.Webdav ? AuthType.Basic : AuthType.None);
        if (!options.Address.Contains("://", StringComparison.Ordinal))
            return BuildNonUrl(options, resolvedAuth);

        if (!Uri.TryCreate(options.Address, UriKind.Absolute, out Uri url))
            throw new ArgumentException($"Invalid URL: {options.Address}", nameof(options.Address));

        string scheme = url.Scheme;
        if (!Enum.TryParse(scheme, true, out ProtocolType _))
            throw new ArgumentException($"Invalid protocol scheme: {scheme}", nameof(options.Address));

        string host = url.Host.TrimStart('[').TrimEnd(']');
        uint? fallbackPort = options.Protocol.DefaultPort();
        uint port = url.Port != -1 ? (uint)url.Port : (fallbackPort ?? throw new ArgumentException("Port required for URL", nameof(options.Address)));

        var path = url.AbsolutePath;
        if (!string.IsNullOrEmpty(url.Query))
        {
            path += "?" + url.Query.TrimStart('?');
        }

        string username = string.Empty;
        string password = string.Empty;
        string userInfo = url.UserInfo;

        if (string.IsNullOrEmpty(userInfo))
        {
            username = string.Empty;
            password = string.Empty;
        }
        else
        {
            int colonPos = userInfo.IndexOf(':');
            if (colonPos >= 0)
            {
                username = Uri.UnescapeDataString(userInfo[..colonPos]);
                password = Uri.UnescapeDataString(userInfo[(colonPos + 1)..]);
            }
            else
            {
                username = Uri.UnescapeDataString(userInfo);
                password = string.Empty;
            }
        }

        return new ClientEndpoint()
        {
            LocalAddr = host,
            LocalPort = port,
            LocalPath = path ?? string.Empty,

            ProxyProtocol = ProxyProtocol.None,
            Description = string.IsNullOrEmpty(options.Name) ? null : options.Name,
            LocalProto = options.Protocol,
            Auth = resolvedAuth,

            Nodelay = true,
            Username = username,
            Password = password,

            Acl = { options.Acl ?? [] },
            Headers = { options.Headers ?? [] },
            FilterRules = { NormalizeRules(options.Rules) }
        };
    }

    private static ClientEndpoint BuildNonUrl(CloudPubPublishOptions options, AuthType resolvedAuth)
    {
        ClientEndpoint endpoint = new ClientEndpoint()
        {
            ProxyProtocol = ProxyProtocol.None,
            Description = string.IsNullOrEmpty(options.Name) ? null : options.Name,
            LocalProto = options.Protocol,
            Auth = resolvedAuth,

            Nodelay = true,
            Username = string.Empty,
            Password = string.Empty,

            Acl = { options.Acl ?? [] },
            Headers = { options.Headers ?? [] },
            FilterRules = { NormalizeRules(options.Rules) }
        };

        switch (options.Protocol)
        {
            case ProtocolType.OneC:
            case ProtocolType.Minecraft:
            case ProtocolType.Webdav:
                {
                    endpoint.LocalAddr = options.Address;
                    endpoint.LocalPort = 0;
                    endpoint.LocalPath = string.Empty;
                    return endpoint;
                }

            default:
                {
                    if (uint.TryParse(options.Address, out uint localPort))
                    {
                        endpoint.LocalAddr = "localhost";
                        endpoint.LocalPort = localPort;
                        endpoint.LocalPath = string.Empty;
                        return endpoint;
                    }

                    int slashIdx = options.Address.IndexOf('/');
                    endpoint.LocalAddr = slashIdx >= 0 ? options.Address[..slashIdx] : options.Address;
                    endpoint.LocalPath = slashIdx >= 0 ? options.Address[slashIdx..] : string.Empty;

                    if (endpoint.LocalAddr.Contains(':', StringComparison.Ordinal))
                    {
                        if (!TryParseHostPort(endpoint.LocalAddr, out string localAddr, out localPort))
                            throw new ArgumentException($"Invalid options.Address: {options.Address}", nameof(options.Address));

                        endpoint.LocalAddr = localAddr;
                        endpoint.LocalPort = localPort;
                    }
                    else
                    {
                        if (options.Protocol.DefaultPort() is not { } defaultPort)
                            throw new ArgumentException($"Invalid options.Address: {options.Address}", nameof(options.Address));

                        endpoint.LocalPort = defaultPort;
                    }

                    return endpoint;
                }
        }
    }

    private static IEnumerable<FilterRule> NormalizeRules(IReadOnlyCollection<FilterRule>? rules)
    {
        if (rules is not { Count: > 0 })
            return [];

        int i = 0;
        return rules.Select(rule => new FilterRule()
        {
            Order = i++,
            ActionType = rule.ActionType,
            Data = rule.Data ?? string.Empty,
            ActionValue = !string.IsNullOrEmpty(rule.ActionValue) ? rule.ActionValue : null
        });
    }

    private static bool TryParseHostPort(ReadOnlySpan<char> hostPort, out string host, out uint port)
    {
        host = string.Empty;
        port = 0;

        int lastColon = hostPort.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == hostPort.Length - 1)
            return false;

        ReadOnlySpan<char> hostSpan = hostPort[..lastColon];
        ReadOnlySpan<char> portSpan = hostPort[(lastColon + 1)..];

        if (hostSpan.StartsWith("[") && hostSpan.EndsWith("]"))
            hostSpan = hostSpan[1..^1];

        if (!uint.TryParse(portSpan, out uint parsedPort))
            return false;

        string parsedHost = hostSpan.ToString();
        if (Uri.CheckHostName(parsedHost) == UriHostNameType.Unknown)
            return false;

        host = parsedHost;
        port = parsedPort;
        return true;
    }
}
