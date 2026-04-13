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

using CloudPub.Protocol;

namespace CloudPub.Components;

/// <summary>
/// Manages per-channel relays that connect CloudPub data channels to local services.
/// </summary>
public interface IRelaysManager
{
    /// <summary>
    /// Opens a relay for an incoming data channel targeting a local <see cref="CloudPub.Protocol.ServerEndpoint"/>.
    /// </summary>
    /// <param name="channelId">Server-assigned channel id.</param>
    /// <param name="endpoint">Local bind/connect parameters from the server.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IDataChannelRelay?> CreateDataChannel(uint channelId, ServerEndpoint endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forwards tunneled bytes to the relay for <paramref name="channelId"/>.
    /// </summary>
    /// <param name="channelId">Target channel id.</param>
    /// <param name="data">Payload to write locally.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<uint> WriteDataChannel(uint channelId, byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tears down and disposes the relay for <paramref name="channelId"/>.
    /// </summary>
    /// <param name="channelId">Channel id to close.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task DeleteDataChannel(uint channelId, CancellationToken cancellationToken = default);
}
