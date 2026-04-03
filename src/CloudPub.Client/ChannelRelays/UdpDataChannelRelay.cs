using CloudPub.Components;
using System.Net.Sockets;

namespace CloudPub.ChannelRelays;

/// <summary>
/// <see cref="CloudPub.Components.IDataChannelRelay"/> implementation that forwards tunneled datagrams to a local UDP socket.
/// </summary>
public class UdpDataChannelRelay : IDataChannelRelay
{
    private readonly UdpClient _udpClient;
    private readonly SemaphoreSlim _writeLock;

    /// <inheritdoc />
    public uint ChannelId { get; }

    /// <inheritdoc />
    public uint TotalConsumed { get; private set; }

    private UdpDataChannelRelay(uint channelId, UdpClient udpClient)
    {
        ChannelId = channelId;
        _udpClient = udpClient;
        _writeLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Binds an outbound UDP association to <paramref name="localAddr"/>:<paramref name="localPort"/>.
    /// </summary>
    /// <param name="channelId">Server-assigned channel id.</param>
    /// <param name="localAddr">Hostname or IP of the local UDP service.</param>
    /// <param name="localPort">UDP port of the local service.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task<UdpDataChannelRelay> CreateAsync(
        uint channelId, string localAddr, uint localPort, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UdpClient udp = new UdpClient();

        udp.Connect(localAddr, (int)localPort);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new UdpDataChannelRelay(channelId, udp));
    }

    /// <inheritdoc />
    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _udpClient.SendAsync(data, data.Length).ConfigureAwait(false);
            TotalConsumed += (uint)data.Length;
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
            _udpClient.Dispose();
            GC.SuppressFinalize(this);
        }
        catch
        {
            _ = 0xBAD + 0xC0DE;
        }

        await Task.Yield();
    }
}
