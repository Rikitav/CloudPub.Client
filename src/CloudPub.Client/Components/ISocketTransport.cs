using CloudPub.Options;
using CloudPub.Protocol;

namespace CloudPub.Components;

/// <summary>
/// Abstraction over the CloudPub agent connection (typically a WebSocket) used to send and receive protobuf messages.
/// </summary>
public interface ISocketTransport : IAsyncDisposable
{
    /// <summary>
    /// Gets the client options associated with this transport.
    /// </summary>
    CloudPubClientOptions Options { get; }

    /// <summary>
    /// Establishes the connection and completes the handshake.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts receiving messages and dispatching them to <paramref name="exchanger"/>.
    /// </summary>
    /// <param name="exchanger">Handler for inbound messages.</param>
    /// <param name="cancellationToken">A token to cancel the receive loop.</param>
    Task StartReceivingAsync(IMessageExchanger exchanger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SendAsync(Message message, CancellationToken cancellationToken = default);
}
