using CloudPub.Options;
using Protocol;

namespace CloudPub.Components;

public interface ICloudPubClient : IAsyncDisposable, IDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task<Endpoint> PublishAsync(CloudPubPublishOptions options, CancellationToken cancellationToken = default);
    Task UnpublishAsync(Endpoint endpoint, CancellationToken cancellationToken = default);

    Task CreateDataChannel(uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken = default);
    Task WriteDataChannel(uint channelId, byte[] data, CancellationToken cancellationToken = default);
    Task DeleteDataChannel(uint channelId, CancellationToken cancellationToken = default);
}
