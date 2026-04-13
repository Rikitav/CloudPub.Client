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
using System.Net.Sockets;

namespace CloudPub.ChannelRelays;

/// <summary>
/// <see cref="CloudPub.Components.IDataChannelRelay"/> implementation that forwards tunneled bytes to a local TCP socket.
/// </summary>
public class TcpDataChannelRelay : IDataChannelRelay
{
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock;

    /// <inheritdoc />
    public uint ChannelId { get; }

    /// <inheritdoc />
    public uint TotalConsumed { get; private set; }

    private TcpDataChannelRelay(uint channelId, NetworkStream stream)
    {
        ChannelId = channelId;
        _stream = stream;
        _writeLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Connects to <paramref name="localAddr"/>:<paramref name="localPort"/> and wraps the stream in a relay instance.
    /// </summary>
    /// <param name="channelId">Server-assigned channel id.</param>
    /// <param name="localAddr">Hostname or IP of the local service.</param>
    /// <param name="localPort">TCP port of the local service.</param>
    /// <param name="cancellationToken">A token to cancel the connect operation.</param>
    public static async Task<TcpDataChannelRelay> CreateAsync(
        uint channelId, string localAddr, uint localPort, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TcpClient tcp = new TcpClient();
        await tcp.ConnectAsync(localAddr, (int)localPort).ConfigureAwait(false);
        tcp.NoDelay = true;

        cancellationToken.ThrowIfCancellationRequested();
        NetworkStream stream = tcp.GetStream();
        return new TcpDataChannelRelay(channelId, stream);
    }

    /// <inheritdoc />
    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(data.AsMemory(0, data.Length), cancellationToken).ConfigureAwait(false);
            TotalConsumed += (uint)data.Length;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[8192]; // DATA_BUFFER_SIZE

        try
        {
            int bytesRead = await _stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0) // EOF
                return ReadOnlyMemory<byte>.Empty;

            return buffer.AsMemory(0, bytesRead);
        }
        catch (OperationCanceledException)
        {
            // cancelled
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            _stream.Dispose();
            GC.SuppressFinalize(this);
        }
        catch
        {
            _ = 0xBAD + 0xC0DE;
        }

        await Task.Yield();
    }
}
