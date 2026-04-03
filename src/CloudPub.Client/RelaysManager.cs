using CloudPub.ChannelRelays;
using CloudPub.Components;
using CloudPub.Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using ProtocolType = CloudPub.Protocol.ProtocolType;

namespace CloudPub;

/// <summary>
/// Thread-safe registry of <see cref="CloudPub.Components.IDataChannelRelay"/> instances keyed by server-assigned channel id,
/// selecting TCP or UDP relays based on the endpoint protocol.
/// </summary>
public class RelaysManager : IRelaysManager
{
    private readonly SemaphoreSlim RelayAddLock = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<uint, IDataChannelRelay> ChannelIdToRelayMap = [];

    /// <summary>
    /// Opens a local relay for the given channel id according to <paramref name="endpoint"/> (TCP by default, UDP when requested).
    /// </summary>
    /// <param name="channelId">Server-assigned data channel identifier.</param>
    /// <param name="endpoint">Describes the local address and protocol to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task CreateDataChannel(uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        await RelayAddLock.WaitAsync(cancellationToken);

        try
        {
            if (endpoint?.Client is null)
                return;

            switch (endpoint.Client.LocalProto)
            {
                case ProtocolType.Udp:
                    {
                        string localAddr = endpoint.Client.LocalAddr;
                        uint localPort = (ushort)endpoint.Client.LocalPort;

                        UdpDataChannelRelay relay = await UdpDataChannelRelay.CreateAsync(channelId, localAddr, localPort, CancellationToken.None)
                            .ConfigureAwait(false);

                        ChannelIdToRelayMap.TryAdd(channelId, relay);
                        break;
                    }

                default:
                    {
                        string localAddr = endpoint.Client.LocalAddr;
                        uint localPort = (ushort)endpoint.Client.LocalPort;

                        TcpDataChannelRelay relay = await TcpDataChannelRelay.CreateAsync(channelId, localAddr, localPort, CancellationToken.None)
                            .ConfigureAwait(false);

                        ChannelIdToRelayMap.TryAdd(channelId, relay);
                        break;
                    }
            }
        }
        finally
        {
            Debug.WriteLine("Tunnel created!");
            RelayAddLock.Release();
        }
    }

    /// <summary>
    /// Writes payload bytes to the relay for <paramref name="channelId"/>, if a relay exists.
    /// </summary>
    /// <param name="channelId">Target data channel identifier.</param>
    /// <param name="data">Bytes to forward to the local socket.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<uint> WriteDataChannel(uint channelId, byte[] data, CancellationToken cancellationToken = default)
    {
        await RelayAddLock.WaitAsync(cancellationToken);

        try
        {
            if (!ChannelIdToRelayMap.TryGetValue(channelId, out IDataChannelRelay? relay))
                return 0;

            await relay.WriteAsync(data, cancellationToken);
            return relay.TotalConsumed;
        }
        finally
        {
            Debug.WriteLine("Data received!");
            RelayAddLock.Release();
        }
    }

    /// <summary>
    /// Disposes and removes the relay for <paramref name="channelId"/>, if present.
    /// </summary>
    /// <param name="channelId">Data channel identifier to tear down.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task DeleteDataChannel(uint channelId, CancellationToken cancellationToken = default)
    {
        await RelayAddLock.WaitAsync(cancellationToken);

        try
        {
            if (!ChannelIdToRelayMap.TryGetValue(channelId, out IDataChannelRelay? relay))
                return;

            await relay.DisposeAsync();
        }
        finally
        {
            RelayAddLock.Release();
        }
    }
}
