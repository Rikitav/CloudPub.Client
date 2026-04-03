using CloudPub.Protocol;

namespace CloudPub.Components;

/// <summary>
/// Manages per-channel relays that connect CloudPub data channels to local services.
/// </summary>
public interface IRelaysManager
{
    /// <summary>
    /// Opens a relay for an incoming data channel targeting a local <see cref="CloudPub.Protocol.ServerEndpoint"/>.
    /// </summary>
    /// <param name="channelId">Server-assigned channel id.</param>
    /// <param name="endpoint">Local bind/connect parameters from the server.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task CreateDataChannel(uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forwards tunneled bytes to the relay for <paramref name="channelId"/>.
    /// </summary>
    /// <param name="channelId">Target channel id.</param>
    /// <param name="data">Payload to write locally.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task WriteDataChannel(uint channelId, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tears down and disposes the relay for <paramref name="channelId"/>.
    /// </summary>
    /// <param name="channelId">Channel id to close.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DeleteDataChannel(uint channelId, CancellationToken cancellationToken = default);
}
