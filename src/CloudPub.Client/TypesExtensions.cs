// The MIT License (MIT)
// 
// CloudPub.Client
// Copyright 2026 © Rikitav Tim4ik
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the “Software”), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using CloudPub.Components;
using CloudPub.Options;
using CloudPub.Protocol;
using ProtocolType = CloudPub.Protocol.ProtocolType;

namespace CloudPub;
#pragma warning disable RCS1194 // Implement exception constructors

/// <summary>
/// Exception thrown when the CloudPub server returns an error, the handshake fails, or an unexpected protocol condition occurs.
/// </summary>
/// <param name="message">The error message.</param>
/// <param name="inner">The inner exception, if any.</param>
public sealed class CloudPubException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// High-level operations for <see cref="CloudPub.Components.ICloudPubClient"/>: publish, stop, unpublish, list, and clear endpoints.
/// </summary>
public static class CloudPubClientExtensions
{
    /// <summary>
    /// Starts exposing a local service through CloudPub using the specified publish options.
    /// </summary>
    /// <param name="client">The connected client.</param>
    /// <param name="options">Local binding, protocol, and metadata for the new endpoint.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The registered endpoint description returned by the server.</returns>
    public static async Task<Endpoint> PublishAsync(this ICloudPubClient client, CloudPubPublishOptions options, CancellationToken cancellationToken = default)
    {
        Message responce = await client.ExchangeAsync(
            new Message { EndpointStart = options.CreateCleintEndpoint() },
            Message.MessageOneofCase.EndpointAck,
            cancellationToken).ConfigureAwait(false);

        return responce.EndpointAck.ToEndpoint();
    }

    /// <summary>
    /// Stops traffic for an endpoint without removing its registration.
    /// </summary>
    /// <param name="client">The connected client.</param>
    /// <param name="endpoint">The endpoint to stop.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async Task StopAsync(this ICloudPubClient client, Endpoint endpoint, CancellationToken cancellationToken = default)
    {
        await client.ExchangeAsync(
            new Message { EndpointStop = new EndpointStop { Guid = endpoint.Guid } },
            Message.MessageOneofCase.EndpointAck,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes an endpoint registration and marks the in-memory <paramref name="endpoint"/> status as offline.
    /// </summary>
    /// <param name="client">The connected client.</param>
    /// <param name="endpoint">The endpoint to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async Task UnpublishAsync(this ICloudPubClient client, Endpoint endpoint, CancellationToken cancellationToken = default)
    {
        await client.ExchangeAsync(
            new Message { EndpointRemove = new EndpointRemove { Guid = endpoint.Guid } },
            Message.MessageOneofCase.EndpointRemoveAck,
            cancellationToken).ConfigureAwait(false);

        endpoint.Status = "offline";
    }

    /// <summary>
    /// Clears all endpoint registrations on the server.
    /// </summary>
    /// <param name="client">The connected client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async Task CleanAsync(this ICloudPubClient client, CancellationToken cancellationToken = default)
    {
        await client.ExchangeAsync(
            new Message { EndpointClear = new EndpointClear() },
            Message.MessageOneofCase.EndpointClearAck,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the current list of registered endpoints from the server.
    /// </summary>
    /// <param name="client">The connected client.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A sequence of endpoint descriptors.</returns>
    public static async Task<IEnumerable<Endpoint>> ListAsync(this ICloudPubClient client, CancellationToken cancellationToken = default)
    {
        Message message = await client.ExchangeAsync(
            new Message { EndpointList = new EndpointList() },
            Message.MessageOneofCase.EndpointListAck,
            cancellationToken).ConfigureAwait(false);

        return message.EndpointListAck.Endpoints.Select(x => x.ToEndpoint());
    }
}

/// <summary>
/// Helpers for protocol wire names, default ports, URI construction, and host/port formatting.
/// </summary>
public static class TypesExtensions
{
    /// <summary>
    /// Returns the wire-format protocol name used in URLs and API payloads.
    /// </summary>
    /// <param name="protocol">The protocol enum value.</param>
    /// <returns>A lowercase identifier (e.g. <c>http</c>, <c>tcp</c>).</returns>
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

    /// <summary>
    /// Returns a common default port for well-known protocols, or <c>null</c> when none is defined.
    /// </summary>
    /// <param name="protocol">The protocol enum value.</param>
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

    /// <summary>
    /// Builds a <see cref="Uri"/> for the public remote address of a <see cref="ServerEndpoint"/>.
    /// </summary>
    /// <param name="ep">Server endpoint metadata from a protocol message.</param>
    public static Uri ToEndpointUri(this ServerEndpoint ep)
    {
        ProtocolType proto = ep.RemoteProto;
        string wire = proto.ToWireName();
        return new Uri($"{wire}://{ep.RemoteAddr}:{ep.RemotePort}");
    }

    /// <summary>
    /// Maps a <see cref="ServerEndpoint"/> to the library's <see cref="Endpoint"/> view model.
    /// </summary>
    /// <param name="ep">Server endpoint metadata from a protocol message.</param>
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

    /// <summary>
    /// Formats a <see cref="ClientEndpoint"/> int url string like <c>http://localhost:8080/path</c>, omitting port when zero and path when empty.
    /// </summary>
    /// <param name="clientEndpoint"></param>
    /// <returns></returns>
    public static string ToLocalEndpointUrl(this ClientEndpoint clientEndpoint)
    {
        string scheme = clientEndpoint.LocalProto.ToWireName();
        string host = clientEndpoint.LocalAddr.Contains(':') ? $"[{clientEndpoint.LocalAddr}]" : clientEndpoint.LocalAddr;
        string portPart = clientEndpoint.LocalPort != 0 ? $":{clientEndpoint.LocalPort}" : string.Empty;
        string pathPart = !string.IsNullOrEmpty(clientEndpoint.LocalPath) ? clientEndpoint.LocalPath : string.Empty;
        return $"{scheme}://{host}{portPart}{pathPart}";
    }

    /// <summary>
    /// Formats a URI as <c>host:port</c>, normalizing default HTTP/HTTPS ports when the port is omitted.
    /// </summary>
    /// <param name="url">The base URI (typically the CloudPub server URL).</param>
    public static string ToHostAndPort(this Uri url)
        => $"{GetHost(url)}:{GetPort(url)}";

    private static string GetHost(Uri serverUri) => serverUri.Host.TrimStart('[').TrimEnd(']');
    private static int GetPort(Uri serverUri) => serverUri.IsDefaultPort ? (serverUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? 80 : 443) : serverUri.Port;
}

/// <summary>
/// Builds a protobuf <see cref="ClientEndpoint"/> from high-level <see cref="CloudPub.Options.CloudPubPublishOptions"/>.
/// </summary>
public static class CloudPubClientOptionsExtensions
{
    /// <summary>
    /// Converts publish options into a <see cref="ClientEndpoint"/> suitable for <c>EndpointStart</c> messages,
    /// supporting both URL-style and shorthand address formats.
    /// </summary>
    /// <param name="options">User-specified protocol, address, auth, and optional rules.</param>
    /// <returns>The configured local endpoint description.</returns>
    public static ClientEndpoint CreateCleintEndpoint(this CloudPubPublishOptions options)
    {
        AuthType resolvedAuth = options.Auth ?? (options.Protocol == ProtocolType.Webdav ? AuthType.Basic : AuthType.None);
        if (!options.Address.Contains("://", StringComparison.Ordinal))
            return BuildNonUrl(options, resolvedAuth);

        if (!Uri.TryCreate(options.Address, UriKind.Absolute, out Uri url))
            throw new ArgumentException($"Invalid URL: {options.Address}", nameof(options.Address));

        string scheme = url.Scheme;
        if (!System.Enum.TryParse(scheme, true, out ProtocolType _))
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
