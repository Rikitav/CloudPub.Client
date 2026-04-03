namespace CloudPub.Components;

/// <summary>
/// Abstraction for forwarding bytes between CloudPub data channels and a local TCP or UDP socket.
/// </summary>
public interface IDataChannelRelay : IAsyncDisposable
{
    /// <summary>
    /// Gets the server-assigned channel identifier for this relay.
    /// </summary>
    public uint ChannelId { get; }

    /// <summary>
    /// Gets the total amount consumed, represented as an unsigned integer.
    /// </summary>
    public uint TotalConsumed { get; }

    /// <summary>
    /// Writes data to the underlying local connection.
    /// </summary>
    /// <param name="data">Payload received from the tunnel.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public Task WriteAsync(byte[] data, CancellationToken cancellationToken = default);
}
