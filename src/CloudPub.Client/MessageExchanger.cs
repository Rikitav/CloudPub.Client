using CloudPub.Components;
using CloudPub.Options;
using Google.Protobuf;
using CloudPub.Protocol;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace CloudPub;

/// <summary>
/// Routes inbound <see cref="CloudPub.Protocol.Message"/> instances from the socket: handles heartbeats, errors,
/// endpoint acknowledgements, and multiplexed data channels via <see cref="CloudPub.Components.IRelaysManager"/>.
/// </summary>
/// <param name="options">Client options (reserved for future use).</param>
/// <param name="relays">Registry used to open, write, and close local data-channel relays.</param>
public sealed class MessageExchanger(CloudPubClientOptions options, IRelaysManager relays) : IMessageExchanger
{
    private readonly Channel<Message> _pendingMessages = Channel.CreateUnbounded<Message>();
    private readonly CloudPubClientOptions _options = options;
    private readonly IRelaysManager _relays = relays;

    /// <summary>
    /// Returns a value task that completes when at least one message may be read from the internal queue.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns><c>true</c> when data may be available; <c>false</c> if the channel completed.</returns>
    public ValueTask<bool> WaitForMessagesAsync(CancellationToken cancellationToken = default)
    {
        return _pendingMessages.Reader.WaitToReadAsync(cancellationToken);
    }

    /// <summary>
    /// Asynchronously enumerates all messages delivered to the pending queue (including protocol messages
    /// not handled specially in <see cref="HandleMessage"/>).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel enumeration.</param>
    public IAsyncEnumerable<Message> ReadMessagesAsync(CancellationToken cancellationToken = default)
    {
        return _pendingMessages.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Processes a single inbound message: replies to heartbeats, completes the queue on fatal errors,
    /// manages endpoint and data-channel messages, and enqueues other messages for consumers.
    /// </summary>
    /// <param name="socket">Transport used to send replies (e.g. heartbeats, acknowledgements).</param>
    /// <param name="messgae">The parsed message from the server.</param>
    /// <param name="cancellationToken">A token to cancel outbound sends.</param>
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

                        _ = CreateDataChannel(socket, channelId, endpoint, cancellationToken);
                        break;
                    }

                case Message.MessageOneofCase.DataChannelEof:
                    {
                        uint channelId = messgae.DataChannelEof.ChannelId;
                        _ = DeleteDataChannel(socket, channelId, cancellationToken);
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

                        _ = WriteDataChannel(socket, channelId, data, cancellationToken);
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

    /// <summary>
    /// Releases resources associated with the exchanger.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
    }

    private async Task CreateDataChannel(ISocketTransport socket, uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        try
        {
            await _relays.CreateDataChannel(channelId, endpoint, cancellationToken);
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

            await socket.SendAsync(exceptionMsg, cancellationToken);
        }
    }

    private async Task DeleteDataChannel(ISocketTransport socket, uint channelId, CancellationToken cancellationToken)
    {
        try
        {
            await _relays.DeleteDataChannel(channelId, cancellationToken);
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

            await socket.SendAsync(exceptionMsg, cancellationToken);
        }
    }

    private async Task WriteDataChannel(ISocketTransport socket, uint channelId, byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            uint totalConsumed = await _relays.WriteDataChannel(channelId, data);
            Message consumedMsg = new Message
            {
                DataChannelAck = new DataChannelAck
                {
                    ChannelId = channelId,
                    Consumed = totalConsumed
                }
            };

            await socket.SendAsync(consumedMsg, cancellationToken).ConfigureAwait(false);
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

            await socket.SendAsync(exceptionMsg, cancellationToken);
        }
    }

    private static bool IsFatal(ErrorKind kind)
        => kind is ErrorKind.Fatal or ErrorKind.AuthFailed;
}