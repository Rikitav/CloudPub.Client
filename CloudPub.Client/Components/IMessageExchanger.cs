using Protocol;

namespace CloudPub.Components;

public interface IMessageExchanger : IAsyncDisposable
{
    ValueTask<bool> WaitForMessagesAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<Message> ReadMessagesAsync(CancellationToken cancellationToken = default);
    Task HandleMessage(ISocketTransport socket, Message messgae, CancellationToken cancellationToken);
}
