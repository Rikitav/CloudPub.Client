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

namespace CloudPub;

/// <summary>
/// Default implementation of <see cref="ICloudPubClient"/> that connects over WebSocket,
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
    /// <param name="rules"></param>
    public CloudPubClient(CloudPubClientOptions options, ICloudPubRules rules)
    {
        _options = options;
        _relays = new RelaysManager(rules);
        _socket = new SocketTransport(options, rules);
        _exchanger = new MessageExchanger(options, rules, _relays);
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
    /// <param name="type">One or more expected response message kinds.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The first matching response message.</returns>
    public async Task<Message> ExchangeAsync(Message request, Message.MessageOneofCase type, CancellationToken cancellationToken = default)
    {
        await _socket.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await _exchanger.WaitForMessageAsync(type, cancellationToken).ConfigureAwait(false);
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
