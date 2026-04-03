using CloudPub.Options;
using Protocol;

namespace CloudPub.Components;

public interface ISocketTransport : IAsyncDisposable
{
    CloudPubClientOptions Options { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task StartReceivingAsync(IMessageExchanger exchanger, CancellationToken cancellationToken = default);
    Task SendAsync(Message message, CancellationToken cancellationToken = default);
}
