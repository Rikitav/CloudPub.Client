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
using Microsoft.AspNetCore.Http;
using CloudPub.Services;
using System.Threading.Channels;
using System.Diagnostics;

namespace CloudPub.ChannelRelays;

internal sealed class TcpToHttpRelayChannel(IServiceProvider services, HttpRelayDispatcher dispatcher) : IDataChannelRelay
{
    private readonly HttpRequestAccumulator _accumulator = new HttpRequestAccumulator(services);
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
    private readonly HttpRelayDispatcher _dispatcher = dispatcher;
    private readonly Channel<byte[]> _responseChannel = Channel.CreateUnbounded<byte[]>();

    /// <inheritdoc />
    public uint ChannelId { get; }

    /// <inheritdoc />
    public uint TotalConsumed { get; private set; }

    /// <inheritdoc />
    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            TotalConsumed += (uint)data.Length;
            Debug.WriteLine($"CloudPub TCP->HTTP relay consumed bytes={data.Length}, total={TotalConsumed}");
            
            foreach (HttpContext context in _accumulator.Accumulate(data))
            {
                Debug.WriteLine($"CloudPub TCP->HTTP relay enqueue request: {context.Request.Method} {context.Request.Path}");
                await _dispatcher.RequestAsync(context, cancellationToken);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] response = await _dispatcher.Responces.ReadAsync(cancellationToken);
            Debug.WriteLine($"CloudPub TCP->HTTP relay sending response bytes={response.Length}");
            return response;
        }
        catch (OperationCanceledException)
        {
            // cancelled
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            _accumulator.Dispose();
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CloudPub TCP->HTTP relay dispose failed: {ex}");
        }

        await Task.Yield();
    }
}
