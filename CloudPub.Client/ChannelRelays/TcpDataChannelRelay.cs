using CloudPub.Components;
using System.Net.Sockets;

namespace CloudPub.ChannelRelays;

public class TcpDataChannelRelay : IDataChannelRelay
{
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock;

    public uint ChannelId { get; }

    private TcpDataChannelRelay(uint channelId, NetworkStream stream)
    {
        ChannelId = channelId;
        _stream = stream;
        _writeLock = new SemaphoreSlim(1, 1);
    }

    public static async Task<TcpDataChannelRelay> CreateAsync(
        uint channelId, string localAddr, uint localPort, CancellationToken cancellationToken)
    {
        TcpClient tcp = new TcpClient();
        await tcp.ConnectAsync(localAddr, (int)localPort).ConfigureAwait(false);
        tcp.NoDelay = true;

        NetworkStream stream = tcp.GetStream();
        return new TcpDataChannelRelay(channelId, stream);
    }

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

    public async ValueTask DisposeAsync()
    {
        try
        {
            _stream.Dispose();
        }
        catch
        {
            _ = 0xBAD + 0xC0DE;
        }
    }
}
