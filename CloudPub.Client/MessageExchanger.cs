using CloudPub.Components;
using CloudPub.Options;
using Protocol;
using System.Diagnostics;
using System.Threading.Channels;

namespace CloudPub;

public sealed class MessageExchanger(CloudPubClientOptions options, CloudPubClient client) : IMessageExchanger
{
    private readonly CloudPubClientOptions _options = options;
    private readonly CloudPubClient _client = client;
    private readonly Channel<Message> _pendingMessages = Channel.CreateUnbounded<Message>();

    public ValueTask<bool> WaitForMessagesAsync(CancellationToken cancellationToken = default)
    {
        return _pendingMessages.Reader.WaitToReadAsync(cancellationToken);
    }

    public IAsyncEnumerable<Message> ReadMessagesAsync(CancellationToken cancellationToken = default)
    {
        return _pendingMessages.Reader.ReadAllAsync(cancellationToken);
    }

    public async Task HandleMessage(ISocketTransport socket, Message messgae, CancellationToken cancellationToken)
    {
        try
        {
            switch (messgae.MessageCase)
            {
                case Message.MessageOneofCase.HeartBeat:
                    {
                        await socket.SendAsync(new Message { HeartBeat = new HeartBeat() }, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                case Message.MessageOneofCase.Error:
                    {
                        if (!IsFatal(messgae.Error.Kind))
                            break;

                        _pendingMessages.Writer.TryComplete(new CloudPubException($"Fatal Error: {messgae.Error.Message}"));
                        break;
                    }

                case Message.MessageOneofCase.EndpointAck:
                    {
                        if (string.IsNullOrEmpty(messgae.EndpointAck.Error))
                        {
                            messgae.EndpointAck.Status = "online";
                            await socket.SendAsync(new Message { EndpointStatus = messgae.EndpointAck }, cancellationToken);
                        }
                        else
                        {
                            Debug.WriteLine("SERVER ERROR: {0}", messgae.EndpointAck.Error);
                        }

                        _pendingMessages?.Writer.TryWrite(messgae);
                        break;
                    }

                case Message.MessageOneofCase.CreateDataChannelWithId:
                    {
                        uint channelId = messgae.CreateDataChannelWithId.ChannelId;
                        ServerEndpoint endpoint = messgae.CreateDataChannelWithId.Endpoint;

                        _ = Task.Run(async () => await _client.CreateDataChannel(channelId, endpoint), CancellationToken.None);
                        break;
                    }

                case Message.MessageOneofCase.DataChannelEof:
                    {
                        uint channelId = messgae.DataChannelEof.ChannelId;
                        _ = Task.Run(async () => await _client.DeleteDataChannel(channelId, cancellationToken), CancellationToken.None);
                        break;
                    }

                case Message.MessageOneofCase.DataChannelAck:
                    {
                        /*
                        if (_tcpRelays.TryGetValue(messgae.DataChannelAck.ChannelId, out var relay))
                        {
                            relay.AddCapacity(messgae.DataChannelAck.Consumed);
                        }
                        */

                        break;
                    }

                case Message.MessageOneofCase.DataChannelData:
                    {
                        uint channelId = messgae.DataChannelData.ChannelId;
                        byte[] data = messgae.DataChannelData.Data.ToArray();

                        _ = Task.Run(async () => await _client.WriteDataChannel(channelId, data, cancellationToken), CancellationToken.None);
                        break;
                    }

                default:
                    {
                        _pendingMessages?.Writer.TryWrite(messgae);
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            _pendingMessages?.Writer.TryComplete(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
    }

    private static bool IsFatal(ErrorKind kind) => kind is ErrorKind.Fatal or ErrorKind.AuthFailed;
}