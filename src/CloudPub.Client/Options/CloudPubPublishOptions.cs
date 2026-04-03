using CloudPub.Protocol;

namespace CloudPub.Options;

/// <summary>
/// Describes a local service to expose through CloudPub: protocol, address or URL, optional auth, and traffic rules.
/// </summary>
public class CloudPubPublishOptions
{
    /// <summary>
    /// Gets or sets the application-level protocol for the published endpoint.
    /// </summary>
    public ProtocolType Protocol { get; set; } = ProtocolType.Https;

    /// <summary>
    /// Gets or sets the authentication mode; when <c>null</c>, a default is chosen (e.g. Basic for WebDAV).
    /// </summary>
    public AuthType? Auth { get; set; } = null;

    /// <summary>
    /// Gets or sets the local bind address: port number, <c>host:port</c>, host with path, or a full URL depending on protocol.
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// Gets or sets a human-readable name stored as the endpoint description.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets optional access control entries for the endpoint.
    /// </summary>
    public IReadOnlyCollection<Acl>? Acl { get; set; }

    /// <summary>
    /// Gets or sets optional HTTP headers to associate with the published service.
    /// </summary>
    public IReadOnlyCollection<Header>? Headers { get; set; }

    /// <summary>
    /// Gets or sets optional filter rules applied to traffic for this endpoint.
    /// </summary>
    public IReadOnlyCollection<FilterRule>? Rules { get; set; }
}
