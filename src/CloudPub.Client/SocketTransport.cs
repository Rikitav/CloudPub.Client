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
using CloudPub.Options;
using CloudPub.Protocol;
using Google.Protobuf;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace CloudPub;

/// <summary>
/// WebSocket-based transport that performs the agent handshake, sends binary protobuf frames,
/// and runs a receive loop dispatching messages to an <see cref="IMessageExchanger"/>.
/// </summary>
/// <param name="options">Server URI, credentials, timeouts, and agent metadata.</param>
/// <param name="authFacility"></param>
public class SocketTransport(CloudPubClientOptions options, IAuthFacility authFacility) : ISocketTransport
{
    private const string DefaultClientVersion = "3.0.2";
    private const string WebSocketPath = "/endpoint/v3";

    private readonly IAuthFacility _authFacility = authFacility;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    private CancellationTokenSource? _cancellation;
    private ClientWebSocket? _socket = null;
    //private Task? _hartbeatTask;
    private Task? _receiveTask;

    /// <summary>
    /// Gets the options this transport was constructed with (token may be updated after handshake).
    /// </summary>
    public CloudPubClientOptions Options { get; } = options;

    /// <summary>
    /// Connects to the CloudPub WebSocket endpoint, completes the agent hello/ack handshake (following redirects),
    /// and stores the session token on success.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    //[DebuggerStepThrough]
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_socket is { State: WebSocketState.Open })
            throw new InvalidOperationException("Already connected.");

        Uri serverUri = Options.ServerUri ?? new Uri("https://cloudpub.ru");
        string hostAndPort = serverUri.ToHostAndPort();
        string? agentId = await _authFacility.TryLoadAgentIdAsync(true);

        CancellationTokenSource? connectCancellation = null;
        while (connectCancellation?.IsCancellationRequested is not true)
        {
            await CleanupSessionAsync(connectCancellation).ConfigureAwait(false);
            connectCancellation?.Dispose();
            connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _socket = new ClientWebSocket();
            _socket.Options.KeepAliveInterval = TimeSpan.FromHours(10);
            _socket.Options.Proxy = Options.Proxy;

            await _socket.ConnectAsync(BuildWebSocketUri(serverUri, hostAndPort), connectCancellation.Token).ConfigureAwait(false);

            AgentInfo agent = BuildAgentHello(Options, agentId, hostAndPort);
            await SendAsync(new Message { AgentHello = agent }, connectCancellation.Token).ConfigureAwait(false);

            if (string.IsNullOrEmpty(agentId))
            {
                await _authFacility.TrySaveAgentIdAsync(true, agent.AgentId);
            }

            using CancellationTokenSource handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(connectCancellation.Token);
            handshakeCts.CancelAfter(Options.Timeout);

            Message ackMsg = await ReceiveSingleMessageAsync(cancellationToken).ConfigureAwait(false);
            if (ackMsg.MessageCase == Message.MessageOneofCase.Error)
                throw new CloudPubException($"Server error: {ackMsg.Error.Message} (kind={ackMsg.Error.Kind})");

            if (ackMsg.Redirect?.HostAndPort is { Length: > 0 } redirect)
            {
                hostAndPort = redirect;
                continue;
            }

            if (ackMsg.AgentAck?.Token is { Length: > 0 } token)
            {
                Options.Token = token;
                break;
            }

            throw new CloudPubException($"Unexpected handshake message: {ackMsg.MessageCase}");
        }
    }

    /// <summary>
    /// Starts the background receive loop and optionally sends <c>EndpointStartAll</c> when
    /// <see cref="CloudPubClientOptions.ResumeEndpointsOnConnect"/> is enabled.
    /// </summary>
    /// <param name="exchanger">Handler for each decoded inbound message.</param>
    /// <param name="cancellationToken">A token to cancel the receive loop.</param>
    [DebuggerStepThrough]
    public async Task StartReceivingAsync(IMessageExchanger exchanger, CancellationToken cancellationToken = default)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = ReceiveLoopAsync(exchanger, _cancellation.Token);
        //_hartbeatTask = HeartbeatLoopAsync(exchanger, _cancellation.Token);

        if (Options.ResumeEndpointsOnConnect)
            await SendAsync(new Message { EndpointStartAll = new EndpointStartAll() }, _cancellation.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a single binary-encoded protobuf message over the open WebSocket.
    /// </summary>
    /// <param name="message">The message to serialize and send.</param>
    /// <param name="cancellationToken"></param>
    [DebuggerStepThrough]
    public async Task SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected.");

        if (_cancellation != null)
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token, cancellationToken).Token;

        try
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            await _socket.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /*
    [DebuggerStepThrough]
    private async Task HeartbeatLoopAsync(IMessageExchanger exchanger, CancellationToken cancellationToken)
    {
        try
        {
            while (_socket is { State: WebSocketState.Open } && !cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("Heartbeat");
                await Task.Delay(Options.KeepAliveInterval, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                Message msg = new Message() { HeartBeat = new HeartBeat() };
                await SendAsync(msg, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _ = 0xDEADBEEF;
        }
        finally
        {
            Debug.WriteLine("Receiving loop exited! {0}", _socket?.State);
        }
    }
    */

    [DebuggerStepThrough]
    private async Task ReceiveLoopAsync(IMessageExchanger exchanger, CancellationToken cancellationToken)
    {
        try
        {
            while (_socket is { State: WebSocketState.Open } && !cancellationToken.IsCancellationRequested)
            {
                Message msg = await ReceiveSingleMessageAsync(cancellationToken).ConfigureAwait(false);
                await exchanger.HandleMessage(this, msg, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _ = 0xDEADBEEF;
        }
        finally
        {
            Debug.WriteLine("Receiving loop exited! {0}", _socket?.State);
        }
    }

    [DebuggerStepThrough]
    private async Task<Message> ReceiveSingleMessageAsync(CancellationToken cancellationToken)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected.");

        using MemoryStream ms = new MemoryStream();
        byte[] buffer = new byte[16384];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WebSocketReceiveResult result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
                throw new OperationCanceledException("WebSocket closed by server.");

            if (result.MessageType == WebSocketMessageType.Text)
                throw new CloudPubException("Unexpected WebSocket text frame.");

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Message.Parser.ParseFrom(ms.ToArray());
        }
    }

    [DebuggerStepThrough]
    private async Task CleanupSessionAsync(CancellationTokenSource? cancellationTokenSource)
    {
        cancellationTokenSource?.Cancel();
        if (_socket is not null)
        {
            if (_socket.State == WebSocketState.Open)
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    _ = 0xBAD + 0xC0DE;
                }
            }

            _socket.Dispose();
            _socket = null;
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch
            {
                _ = 0xBAD + 0xC0DE;
            }

            _receiveTask = null;
        }
    }

    /// <summary>
    /// Sends a graceful stop message when possible, closes the WebSocket, and releases transport resources.
    /// </summary>
    [DebuggerStepThrough]
    public async ValueTask DisposeAsync()
    {
        _cancellation?.Cancel();
        if (_receiveTask != null)
            await _receiveTask.ConfigureAwait(false);
        
        /*
        if (_hartbeatTask != null)
            await _hartbeatTask.ConfigureAwait(false);
        */

        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await SendAsync(new Message { Stop = new Stop() }, CancellationToken.None).ConfigureAwait(false);
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                _ = 0xBAD + 0xC0DE;
            }
        }

        await CleanupSessionAsync(null).ConfigureAwait(false);
        _sendLock.Dispose();
    }

    [DebuggerStepThrough]
    private static AgentInfo BuildAgentHello(CloudPubClientOptions options, string? agentId, string hostAndPort)
    {
        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "windows" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "darwin" : "linux";

        return new AgentInfo()
        {
            AgentId = string.IsNullOrEmpty(agentId) ? Guid.NewGuid().ToString("D") : agentId,
            Token = options.Token ?? "",
            Hostname = Environment.MachineName,
            Version = string.IsNullOrEmpty(options.ClientVersion) ? DefaultClientVersion : options.ClientVersion,
            Gui = false,
            Platform = platform,
            Hwid = options.Hwid ?? "",
            ServerHostAndPort = hostAndPort,
            Email = options.Email ?? "",
            Password = options.Password ?? "",
            Secondary = false,
            Transient = false,
            IsService = false
        };
    }

    [DebuggerStepThrough]
    private static Uri BuildWebSocketUri(Uri originalServerUri, string hostAndPort)
    {
        if (!Uri.TryCreate("https://" + hostAndPort, UriKind.Absolute, out Uri? hp))
            throw new ArgumentException($"Invalid host:port : {hostAndPort}", nameof(hostAndPort));

        UriBuilder builder = new UriBuilder
        {
            Scheme = originalServerUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss",
            Host = hp.Host,
            Port = hp.Port,
            Path = WebSocketPath
        };

        return builder.Uri;
    }
}
