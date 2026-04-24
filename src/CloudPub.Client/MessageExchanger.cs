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
using CloudPub.Components;
using CloudPub.Options;
using CloudPub.Protocol;
using Google.Protobuf;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace CloudPub;

/// <summary>
/// Routes inbound <see cref="Message"/> instances from the socket: handles heartbeats, errors,
/// endpoint acknowledgements, and multiplexed data channels via <see cref="IRelaysManager"/>.
/// </summary>
/// <param name="options">Client options (reserved for future use).</param>
/// <param name="rules"></param>
/// <param name="relays">Registry used to open, write, and close local data-channel relays.</param>
public sealed class MessageExchanger(CloudPubClientOptions options, ICloudPubRules rules, IRelaysManager relays) : IMessageExchanger
{
    private readonly CloudPubClientOptions _options = options;
    private readonly ICloudPubRules _rules = rules;
    private readonly IRelaysManager _relays = relays;

    private readonly object requestsSync = new object();
    private readonly ConcurrentDictionary<Message.MessageOneofCase, TaskCompletionSource<Message>> _pendingRequests = [];

    /// <inheritdoc/>
    public async Task<Message> WaitForMessageAsync(Message.MessageOneofCase messageType, CancellationToken cancellationToken = default)
    {
        if (messageType == Message.MessageOneofCase.Error)
            throw new ArgumentException("Cannot await Error message");

        TaskCompletionSource<Message> completionSource;
        lock (requestsSync)
        {
            completionSource = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests.TryAdd(messageType, completionSource);
        }

        await using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(() =>
        {
            if (_pendingRequests.TryRemove(messageType, out TaskCompletionSource<Message>? tcs))
                tcs.TrySetCanceled();
        });

        return await completionSource.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task HandleMessage(ISocketTransport socket, Message messgae, CancellationToken cancellationToken)
    {
        try
        {
            if (messgae.MessageCase != Message.MessageOneofCase.EndpointAck)
            {
                lock (requestsSync)
                {
                    if (_pendingRequests.TryRemove(messgae.MessageCase, out TaskCompletionSource<Message> completionSource))
                        completionSource.TrySetResult(messgae);
                }
            }

            switch (messgae.MessageCase)
            {
                default:
                    {
                        Debug.WriteLine("Received unknown\\unsupported\\unhandled message of type '{0}'", messgae.MessageCase);
                        break;
                    }

                case Message.MessageOneofCase.HeartBeat:
                    {
                        await socket.SendAsync(new Message { HeartBeat = new HeartBeat() }, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                case Message.MessageOneofCase.Error:
                    {
                        if (!IsFatal(messgae.Error.Kind))
                            break;

                        lock (requestsSync)
                        {
                            foreach (TaskCompletionSource<Message> completionSource in _pendingRequests.Values)
                                completionSource.TrySetResult(messgae);

                            _pendingRequests.Clear();
                        }

                        break;
                    }

                case Message.MessageOneofCase.EndpointRemove:
                case Message.MessageOneofCase.EndpointRemoveAck:
                    {
                        break;
                    }

                case Message.MessageOneofCase.EndpointStatus:
                case Message.MessageOneofCase.EndpointStatusAck:
                    {
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

                        lock (requestsSync)
                        {
                            if (_pendingRequests.TryRemove(messgae.MessageCase, out TaskCompletionSource<Message> completionSource))
                                completionSource.TrySetResult(messgae);
                        }

                        break;
                    }

                case Message.MessageOneofCase.CreateDataChannelWithId:
                    {
                        uint channelId = messgae.CreateDataChannelWithId.ChannelId;
                        ServerEndpoint endpoint = messgae.CreateDataChannelWithId.Endpoint;

                        CreateDataChannel(socket, channelId, endpoint, cancellationToken);
                        break;
                    }

                case Message.MessageOneofCase.DataChannelEof:
                    {
                        uint channelId = messgae.DataChannelEof.ChannelId;
                        
                        DeleteDataChannel(socket, channelId, cancellationToken);
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

                        WriteDataChannel(socket, channelId, data, cancellationToken);
                        break;
                    }

                case Message.MessageOneofCase.DataChannelDataUdp:
                    {
                        uint channelId = messgae.DataChannelDataUdp.ChannelId;
                        byte[] data = messgae.DataChannelDataUdp.Data.ToArray();

                        WriteDataChannel(socket, channelId, data, cancellationToken);
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CloudPub MessageExchanger failed while handling message '{messgae.MessageCase}': {ex}");
            lock (requestsSync)
            {
                foreach (TaskCompletionSource<Message> completionSource in _pendingRequests.Values)
                    completionSource.TrySetResult(messgae);

                _pendingRequests.Clear();
            }
        }
    }

    /// <summary>
    /// Releases resources associated with the exchanger.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
    }

    private async void CreateDataChannel(ISocketTransport socket, uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken)
    {
        try
        {
            RelayState? relay = await _relays.CreateDataChannel(channelId, endpoint, cancellationToken);
            if (relay is null)
            {
                Debug.WriteLine($"Failed to create relay for channelId={channelId}. Manager returned null.");
                return;
            }

            relay.BeginReadAsync(socket, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await _relays.DeleteDataChannel(channelId, cancellationToken);
        }
        catch (Exception ex)
        {
            Message exceptionMsg = new Message
            {
                DataChannelEof = new DataChannelEof { ChannelId = channelId, Error = ex.Message }
            };

            await socket.SendAsync(exceptionMsg, cancellationToken);
        }
    }

    private async void DeleteDataChannel(ISocketTransport socket, uint channelId, CancellationToken cancellationToken)
    {
        try
        {
            await _relays.DeleteDataChannel(channelId, cancellationToken);
        }
        catch (Exception ex)
        {
            Message exceptionMsg = new Message
            {
                DataChannelEof = new DataChannelEof { ChannelId = channelId, Error = ex.Message }
            };

            await socket.SendAsync(exceptionMsg, cancellationToken);
        }
    }

    private async void WriteDataChannel(ISocketTransport socket, uint channelId, byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            uint totalConsumed = await _relays.WriteDataChannel(channelId, data, cancellationToken).ConfigureAwait(false);
            Message consumedMsg = new Message
            {
                DataChannelAck = new DataChannelAck { ChannelId = channelId, Consumed = totalConsumed }
            };

            await socket.SendAsync(consumedMsg, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Message exceptionMsg = new Message
            {
                DataChannelEof = new DataChannelEof { ChannelId = channelId, Error = ex.Message }
            };

            await socket.SendAsync(exceptionMsg, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsFatal(ErrorKind kind)
        => kind is ErrorKind.Fatal or ErrorKind.AuthFailed;
}