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

using Microsoft.AspNetCore.Http;
using System.Threading.Channels;

namespace CloudPub.Services;

internal sealed class HttpRelayDispatcher
{
    private readonly Channel<HttpContext> _requests = Channel.CreateUnbounded<HttpContext>();
    private readonly Channel<byte[]> _responces = Channel.CreateUnbounded<byte[]>();

    public ChannelReader<HttpContext> Requests => _requests.Reader;
    public ChannelReader<byte[]> Responces => _responces.Reader;

    public ValueTask RequestAsync(HttpContext request, CancellationToken cancellationToken)
        => _requests.Writer.WriteAsync(request, cancellationToken);

    public ValueTask ResponceAsync(byte[] responce, CancellationToken cancellationToken)
        => _responces.Writer.WriteAsync(responce, cancellationToken);
}
