namespace CloudPub.Options;

/// <summary>
/// Configuration for connecting to CloudPub: server URL, session credentials, timeouts, and agent identity fields.
/// </summary>
public sealed class CloudPubClientOptions
{
    /// <summary>
    /// Gets or sets the HTTP(S) base URL of the CloudPub control plane; WebSocket URL is derived from this value.
    /// </summary>
    public Uri ServerUri { get; set; } = new Uri("https://cloudpub.ru");

    /// <summary>
    /// Gets or sets the maximum time to wait for handshake steps after connecting.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether to send <c>EndpointStartAll</c> after the receive loop starts.
    /// </summary>
    public bool ResumeEndpointsOnConnect { get; set; } = true;

    /// <summary>
    /// Gets or sets the session token returned by the server after a successful handshake (may be empty before connect).
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the account email used in the agent hello when authenticating.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the account password used in the agent hello when authenticating.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets a stable agent identifier; a new GUID is used when left empty.
    /// </summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Gets or sets an optional hardware identifier string reported to the server.
    /// </summary>
    public string? Hwid { get; set; }

    /// <summary>
    /// Gets or sets the client version string sent in <c>AgentInfo</c>; a default is used when empty.
    /// </summary>
    public string? ClientVersion { get; set; }
}
