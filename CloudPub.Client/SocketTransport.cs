using CloudPub.Components;
using CloudPub.Options;
using Google.Protobuf;
using Protocol;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;

namespace CloudPub;

public class SocketTransport(CloudPubClientOptions options) : ISocketTransport
{
    private const string DefaultClientVersion = "3.0.2";
    private const string WebSocketPath = "/endpoint/v3";

    private readonly CloudPubClientOptions _options = options;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    private CancellationTokenSource? _cancellation;
    private ClientWebSocket? _socket = null;
    private Task? _receiveTask;

    public CloudPubClientOptions Options => _options;

    [DebuggerStepThrough]
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_socket is { State: WebSocketState.Open })
            throw new InvalidOperationException("Already connected.");

        Uri serverUri = _options.ServerUri ?? new Uri("https://cloudpub.ru");
        string hostAndPort = serverUri.ToHostAndPort();

        while (true)
        {
            await CleanupSessionAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _socket = new ClientWebSocket();
            _socket.Options.KeepAliveInterval = TimeSpan.FromHours(10);

            await _socket.ConnectAsync(BuildWebSocketUri(serverUri, hostAndPort), _cancellation.Token).ConfigureAwait(false);
            await SendAsync(new Message { AgentHello = BuildAgentHello(_options, hostAndPort) }, _cancellation.Token).ConfigureAwait(false);

            using CancellationTokenSource handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
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
                _options.Token = token;
                break;
            }

            throw new CloudPubException($"Unexpected handshake message: {ackMsg.MessageCase}");
        }
    }

    [DebuggerStepThrough]
    public async Task StartReceivingAsync(IMessageExchanger exchanger, CancellationToken cancellationToken = default)
    {
        _receiveTask = ReceiveLoopAsync(exchanger, cancellationToken);
        if (_options.ResumeEndpointsOnConnect)
            await SendAsync(new Message { EndpointStartAll = new EndpointStartAll() }, cancellationToken).ConfigureAwait(false);
    }

    [DebuggerStepThrough]
    public async Task SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (_socket is null || _socket.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected.");

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
    private async Task CleanupSessionAsync()
    {
        _cancellation?.Cancel();

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

        _cancellation?.Dispose();
        _cancellation = null;

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

    [DebuggerStepThrough]
    public async ValueTask DisposeAsync()
    {
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

        await CleanupSessionAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    [DebuggerStepThrough]
    private static AgentInfo BuildAgentHello(CloudPubClientOptions options, string hostAndPort)
    {
        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "windows" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "darwin" : "linux";

        return new AgentInfo()
        {
            AgentId = string.IsNullOrEmpty(options.AgentId) ? Guid.NewGuid().ToString("D") : options.AgentId,
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
