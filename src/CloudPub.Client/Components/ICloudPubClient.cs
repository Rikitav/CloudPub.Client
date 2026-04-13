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
/// Abstraction for a CloudPub agent client that maintains a server connection and performs
/// request/response exchanges over the CloudPub protocol.
/// </summary>
public interface ICloudPubClient : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Establishes the connection and begins processing inbound protocol messages.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message and waits for a response matching one of the specified message kinds.
    /// </summary>
    /// <param name="request">The message to send.</param>
    /// <param name="type">Allowed response message kinds.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The first received message whose kind is in <paramref name="type"/>.</returns>
    Task<Message> ExchangeAsync(Message request, Message.MessageOneofCase type, CancellationToken cancellationToken);
}
