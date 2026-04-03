namespace CloudPub.Components;

public interface IDataChannelRelay : IAsyncDisposable
{
    public uint ChannelId { get; }

    public Task WriteAsync(byte[] data, CancellationToken cancellationToken = default);
}
