using CloudPub.ChannelRelays;
using CloudPub.Components;
using CloudPub.Options;
using Protocol;
using System.Collections.Concurrent;
using System.Diagnostics;
using ProtocolType = Protocol.ProtocolType;

namespace CloudPub;

public class CloudPubClient : ICloudPubClient
{
    private readonly SemaphoreSlim RelayAddLock = new SemaphoreSlim(1, 1); 
    private readonly ConcurrentDictionary<uint, IDataChannelRelay> ChannelIdToRelayMap = [];

    private readonly CloudPubClientOptions _options;
    private readonly ISocketTransport _socket;
    private readonly IMessageExchanger _exchanger;

    public CloudPubClient(CloudPubClientOptions options)
    {
        _options = options;
        _socket = new SocketTransport(_options);
        _exchanger = new MessageExchanger(options, this);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _socket.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _socket.StartReceivingAsync(_exchanger, cancellationToken).ConfigureAwait(false);
    }
    
    public async Task StopAsync(Endpoint endpoint, CancellationToken cancellationToken = default)
    {
        await _socket.SendAsync(new Message { EndpointStop = new EndpointStop { Guid = endpoint.Guid } }, cancellationToken).ConfigureAwait(false);
        await _exchanger.WaitMessageOfType(cancellationToken, Message.MessageOneofCase.EndpointStopAck).ConfigureAwait(false);
    }

    public async Task CleanAsync(CancellationToken cancellationToken = default)
    {
        await _socket.SendAsync(new Message { EndpointClear = new EndpointClear() }, cancellationToken).ConfigureAwait(false);
        await _exchanger.WaitMessageOfType(cancellationToken, Message.MessageOneofCase.EndpointClearAck).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Endpoint>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _socket.SendAsync(new Message { EndpointList = new EndpointList() }, cancellationToken).ConfigureAwait(false);
        Message message = await _exchanger.WaitMessageOfType(cancellationToken, Message.MessageOneofCase.EndpointListAck).ConfigureAwait(false);
        return [.. message.EndpointListAck.Endpoints.Select(x => x.ToEndpoint())];
    }

    public async Task<Endpoint> PublishAsync(CloudPubPublishOptions options, CancellationToken cancellationToken = default)
    {
        Endpoint endpoint = await RegisterAsync(options, cancellationToken).ConfigureAwait(false);
        return endpoint;
    }

    public async Task UnpublishAsync(Endpoint endpoint, CancellationToken cancellationToken = default)
    {
        await _socket.SendAsync(new Message { EndpointRemove = new EndpointRemove { Guid = endpoint.Guid } }, cancellationToken).ConfigureAwait(false);
        await _exchanger.WaitMessageOfType(cancellationToken, Message.MessageOneofCase.EndpointRemoveAck).ConfigureAwait(false);
        endpoint.Status = "offline";
    }

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
                        throw new NotImplementedException();
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
        catch (Exception ex)
        {
            Message exceptionMsg = new Message
            {
                DataChannelEof = new DataChannelEof
                {
                    ChannelId = channelId,
                    Error = ex.Message
                }
            };

            await _socket.SendAsync(exceptionMsg, cancellationToken);
        }
        finally
        {
            Debug.WriteLine("Tunnel created!");
            RelayAddLock.Release();
        }
    }

    public async Task WriteDataChannel(uint channelId, byte[] data, CancellationToken cancellationToken = default)
    {
        await RelayAddLock.WaitAsync(cancellationToken);

        try
        {
            if (!ChannelIdToRelayMap.TryGetValue(channelId, out IDataChannelRelay? relay))
                return;

            await relay.WriteAsync(data, cancellationToken);
            Message consumedMsg = new Message
            {
                DataChannelAck = new DataChannelAck
                {
                    ChannelId = channelId,
                    Consumed = (uint)data.Length
                }
            };

            await _socket.SendAsync(consumedMsg, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Debug.WriteLine("Data received!");
            RelayAddLock.Release();
        }
    }

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

    public void Purge()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cacheDir = Path.Combine(baseDir, "cache", "cloudpub");

        if (Directory.Exists(cacheDir))
            Directory.Delete(cacheDir, recursive: true);
    }

    public async ValueTask DisposeAsync()
    {
        await _exchanger.DisposeAsync().ConfigureAwait(false);
        await _socket.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    private async Task<Endpoint> RegisterAsync(CloudPubPublishOptions options, CancellationToken cancellationToken = default)
    {
        ClientEndpoint clientEndpoint = options.CreateCleintEndpoint();
        await _socket.SendAsync(new Message { EndpointStart = clientEndpoint }, cancellationToken).ConfigureAwait(false);

        Message responce = await _exchanger.WaitMessageOfType(cancellationToken, Message.MessageOneofCase.EndpointAck).ConfigureAwait(false);
        return responce.EndpointAck.ToEndpoint();
    }
}
