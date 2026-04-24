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

using CloudPub.ChannelRelays;
using CloudPub.Protocol;
using Google.Protobuf;
using System.Diagnostics;

namespace CloudPub.Components;

/// <summary>
/// Stores <see cref="IDataChannelRelay"/> instance with stopping token
/// </summary>
/// <param name="channelId"></param>
/// <param name="relay"></param>
public class RelayState(uint channelId, IDataChannelRelay relay) : IAsyncDisposable
{
    private Task? _receivingTask;

    /// <summary>
    /// Server assigned channel ID
    /// </summary>
    public uint ChannelId => channelId;

    /// <summary>
    /// Data relay instance
    /// </summary>
    public IDataChannelRelay Relay => relay;

    /// <summary>
    /// Actions syncking semaphore
    /// </summary>
    public SemaphoreSlim ActionsSync { get; } = new SemaphoreSlim(1, 1);
    
    /// <summary>
    /// Stopping token
    /// </summary>
    public CancellationTokenSource Stoping { get; } = new CancellationTokenSource();

    /// <summary>
    /// Begins to continously read data from socket
    /// </summary>
    /// <param name="socket"></param>
    /// <param name="cancellationToken"></param>
    public async void BeginReadAsync(ISocketTransport socket, CancellationToken cancellationToken)
    {
        if (_receivingTask != null)
            throw new InvalidOperationException("This relay is already receiving data");

        cancellationToken.ThrowIfCancellationRequested();
        _receivingTask = Task.Factory.StartNew(
            () => BeginReadAsyncInternal(socket),
            TaskCreationOptions.LongRunning);
    }

    private async Task BeginReadAsyncInternal(ISocketTransport socket)
    {
        while (!Stoping.IsCancellationRequested)
        {
            try
            {
                ReadOnlyMemory<byte> data = await Relay.ReadAsync(Stoping.Token);
                if (data.IsEmpty)
                    break;

                Message message = new Message();
                if (relay is UdpDataChannelRelay)
                {
                    message.DataChannelDataUdp = new DataChannelDataUdp
                    {
                        ChannelId = ChannelId,
                        Data = ByteString.CopyFrom(data.ToArray())
                    };
                }
                else
                {
                    message.DataChannelData = new DataChannelData()
                    {
                        ChannelId = ChannelId,
                        Data = ByteString.CopyFrom(data.ToArray())
                    };
                }

                await socket.SendAsync(message, Stoping.Token).ConfigureAwait(false);
            }
            catch (Exception exc)
            {
                Debug.WriteLine($"CloudPub relay read loop failed for channelId={ChannelId}: {exc}");
                break;
            }
        }

        Debug.WriteLine($"CloudPub relay read loop stopped for channelId={relay.ChannelId}");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Stoping.Cancel();
        Stoping.Dispose();

        if (_receivingTask != null)
            await _receivingTask;

        ActionsSync.Dispose();
        await relay.DisposeAsync().ConfigureAwait(false);
    }
}

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
    Task<RelayState?> CreateDataChannel(uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forwards tunneled bytes to the relay for <paramref name="channelId"/>.
    /// </summary>
    /// <param name="channelId">Target channel id.</param>
    /// <param name="data">Payload to write locally.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<uint> WriteDataChannel(uint channelId, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tears down and disposes the relay for <paramref name="channelId"/>.
    /// </summary>
    /// <param name="channelId">Channel id to close.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DeleteDataChannel(uint channelId, CancellationToken cancellationToken = default);
}
