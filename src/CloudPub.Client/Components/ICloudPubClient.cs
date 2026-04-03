using CloudPub.Protocol;

namespace CloudPub.Components;

/// <summary>
/// Abstraction for a CloudPub agent client that maintains a server connection and performs
/// request/response exchanges over the CloudPub protocol.
/// </summary>
public interface ICloudPubClient : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Establishes the connection and begins processing inbound protocol messages.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message and waits for a response matching one of the specified message kinds.
    /// </summary>
    /// <param name="request">The message to send.</param>
    /// <param name="types">Allowed response message kinds.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The first received message whose kind is in <paramref name="types"/>.</returns>
    Task<Message> ExchangeAsync(Message request, Message.MessageOneofCase[] types, CancellationToken cancellationToken);
}
