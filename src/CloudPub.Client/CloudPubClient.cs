using CloudPub.Components;
using CloudPub.Options;
using CloudPub.Protocol;
using System.Net;

namespace CloudPub;

/// <summary>
/// Default implementation of <see cref="CloudPub.Components.ICloudPubClient"/> that connects over WebSocket,
/// exchanges protocol messages, and manages local data-channel relays.
/// </summary>
public class CloudPubClient : ICloudPubClient
{
    private readonly CloudPubClientOptions _options;
    private readonly SocketTransport _socket;
    private readonly RelaysManager _relays;
    private readonly MessageExchanger _exchanger;

    private bool isDisposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudPubClient"/> class.
    /// </summary>
    /// <param name="options">Connection and agent configuration.</param>
    public CloudPubClient(CloudPubClientOptions options)
    {
        _options = options;
        _relays = new RelaysManager();
        _socket = new SocketTransport(_options);
        _exchanger = new MessageExchanger(options, _relays);
    }

    /// <summary>
    /// Establishes the WebSocket session and starts the receive loop for inbound messages.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _socket.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _socket.StartReceivingAsync(_exchanger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a request message and waits asynchronously for the first response whose
    /// <see cref="Message.MessageCase"/> matches one of the specified cases.
    /// </summary>
    /// <param name="request">The outbound message to send.</param>
    /// <param name="types">One or more expected response message kinds.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The first matching response message.</returns>
    public async Task<Message> ExchangeAsync(Message request, Message.MessageOneofCase[] types, CancellationToken cancellationToken = default)
    {
        await _socket.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await _exchanger.WaitMessageOfType(cancellationToken, types).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the local on-disk cache directory used by the client under the user's application data folder.
    /// </summary>
    public void Purge()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDir = Path.Combine(baseDir, "cache", "cloudpub");

        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, recursive: true);
    }

    /// <summary>
    /// Releases all resources used by the <see cref="CloudPubClient"/> asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        await Dispose(true).ConfigureAwait(false);
        GC.SuppressFinalize(this);
        isDisposed = true;
    }

    /// <summary>
    /// Releases all resources used by the <see cref="CloudPubClient"/>.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed)
            return;

        Dispose(true).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
        isDisposed = true;
    }

    private async ValueTask Dispose(bool disposing = true)
    {
        if (!disposing)
            return;

        await _exchanger.DisposeAsync().ConfigureAwait(false);
        await _socket.DisposeAsync().ConfigureAwait(false);
    }
}
