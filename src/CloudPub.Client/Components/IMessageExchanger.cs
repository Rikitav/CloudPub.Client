using CloudPub.Protocol;

namespace CloudPub.Components;

/// <summary>
/// Decodes inbound protocol traffic and dispatches work to socket sends and local relays.
/// </summary>
public interface IMessageExchanger : IAsyncDisposable
{
    /// <summary>
    /// Waits until the pending message queue may have items to read.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    ValueTask<bool> WaitForMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates messages queued for application-level consumption.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel enumeration.</param>
    IAsyncEnumerable<Message> ReadMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes one inbound message from the server.
    /// </summary>
    /// <param name="socket">Transport for sending replies.</param>
    /// <param name="messgae">The received message.</param>
    /// <param name="cancellationToken">A token to cancel outbound operations.</param>
    Task HandleMessage(ISocketTransport socket, Message messgae, CancellationToken cancellationToken);
}
