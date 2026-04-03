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
        }
        finally
        {
            _writeLock.Release();
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
