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
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace CloudPub.ChannelRelays;

internal sealed class HttpRequestAccumulator(IServiceProvider services) : IDisposable
{
    private const int InitialBufferSize = 16 * 1024;
    private static readonly byte[] HeaderSeparator = "\r\n\r\n"u8.ToArray();

    private readonly IServiceProvider _services = services;
    private byte[] _buffer = new byte[InitialBufferSize];
    private int _bufferLength;

    public IReadOnlyList<HttpContext> Accumulate(byte[] chunk)
    {
        Append(chunk);
        var contexts = new List<HttpContext>();

        int consumed = 0;
        while (TryParseSingleRequest(_buffer.AsMemory(consumed, _bufferLength - consumed), out HttpContext? context, out int requestBytes))
        {
            consumed += requestBytes;
            contexts.Add(context);
        }

        Compact(consumed);
        return contexts;
    }

    private bool TryParseSingleRequest(ReadOnlyMemory<byte> payload, out HttpContext context, out int consumedBytes)
    {
        context = null!;
        consumedBytes = 0;
        if (payload.IsEmpty)
            return false;

        ReadOnlySequence<byte> sequence = new ReadOnlySequence<byte>(payload);
        SequenceReader<byte> reader = new SequenceReader<byte>(sequence);
        SequencePosition requestStart = reader.Position;
        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> requestLineBytes))
            return false;

        ParseRequestLine(requestLineBytes, out string method, out string pathAndQuery, out string protocol);
        (string path, string query) = ParsePath(pathAndQuery);

        HeaderDictionary headers = [];
        int contentLength = 0;
        bool hasChunkedTransferEncoding = false;

        while (true)
        {
            if (!TryReadLine(ref reader, out ReadOnlySequence<byte> headerLineBytes))
                return false;

            if (headerLineBytes.IsEmpty)
                break;

            ParseHeaderLine(headerLineBytes, headers, ref contentLength, ref hasChunkedTransferEncoding);
        }

        ReadOnlySequence<byte> bodyBytes = default;
        if (hasChunkedTransferEncoding)
        {
            if (!TryReadChunkedBody(ref reader, out byte[] decodedChunkedBody))
                return false;

            bodyBytes = new ReadOnlySequence<byte>(decodedChunkedBody);
            headers.Remove("Transfer-Encoding");
            headers.ContentLength = decodedChunkedBody.Length;
        }
        else
        {
            if (reader.Remaining < contentLength)
                return false;

            if (contentLength > 0)
            {
                SequencePosition bodyEnd = sequence.GetPosition(contentLength, reader.Position);
                bodyBytes = sequence.Slice(reader.Position, bodyEnd);
                reader.Advance(contentLength);
            }
        }

        consumedBytes = (int)sequence.Slice(requestStart, reader.Position).Length;
        context = BuildHttpContext(_services, method, path, query, protocol, headers, bodyBytes.ToArray());
        return true;
    }

    private static (string Path, string Query) ParsePath(string pathAndQuery)
    {
        if (string.IsNullOrWhiteSpace(pathAndQuery))
            return ("/", string.Empty);

        if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out Uri? absoluteUri))
            return (string.IsNullOrEmpty(absoluteUri.AbsolutePath) ? "/" : absoluteUri.AbsolutePath, absoluteUri.Query);

        if (pathAndQuery[0] != '/')
            pathAndQuery = "/" + pathAndQuery;

        int queryIndex = pathAndQuery.IndexOf('?');
        return queryIndex < 0
            ? (pathAndQuery, string.Empty)
            : (pathAndQuery[..queryIndex], pathAndQuery[queryIndex..]);
    }

    private static void ParseRequestLine(ReadOnlySequence<byte> requestLineBytes, out string method, out string pathAndQuery, out string protocol)
    {
        string requestLine = Encoding.ASCII.GetString(requestLineBytes.ToArray());
        string[] parts = requestLine.Split(' ');
        if (parts.Length != 3)
            throw new InvalidDataException("Invalid HTTP request line");

        method = parts[0];
        pathAndQuery = parts[1];
        protocol = parts[2];
    }

    private static void ParseHeaderLine(
        ReadOnlySequence<byte> headerLineBytes,
        HeaderDictionary headers,
        ref int contentLength,
        ref bool hasChunkedTransferEncoding)
    {
        string line = Encoding.ASCII.GetString(headerLineBytes.ToArray());
        int colonIndex = line.IndexOf(':');
        if (colonIndex <= 0)
            return;

        string key = line[..colonIndex].Trim();
        string value = line[(colonIndex + 1)..].Trim();
        headers.Append(key, new StringValues(value));

        if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
        {
            _ = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength);
        }

        if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            hasChunkedTransferEncoding = true;
        }
    }

    private static bool TryReadChunkedBody(ref SequenceReader<byte> reader, out byte[] body)
    {
        ArrayBufferWriter<byte> writer = new ArrayBufferWriter<byte>();

        while (true)
        {
            if (!TryReadLine(ref reader, out ReadOnlySequence<byte> sizeLineBytes))
            {
                body = [];
                return false;
            }

            string sizeLine = Encoding.ASCII.GetString(sizeLineBytes.ToArray());
            string token = sizeLine.Split(';', 2)[0];
            if (!int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int chunkSize))
                throw new InvalidDataException("Invalid chunk size");

            if (chunkSize == 0)
            {
                if (!reader.TryReadTo(out ReadOnlySequence<byte> _, HeaderSeparator, advancePastDelimiter: true))
                {
                    body = [];
                    return false;
                }

                body = writer.WrittenMemory.ToArray();
                return true;
            }

            if (reader.Remaining < chunkSize + 2)
            {
                body = [];
                return false;
            }

            ReadOnlySequence<byte> chunkData = reader.Sequence.Slice(reader.Position, chunkSize);
            chunkData.CopyTo(writer.GetSpan(chunkSize));
            writer.Advance(chunkSize);
            reader.Advance(chunkSize);

            if (!TryReadLine(ref reader, out _))
                throw new InvalidDataException("Malformed chunk terminator");
        }
    }

    private static bool TryReadLine(ref SequenceReader<byte> reader, out ReadOnlySequence<byte> line)
    {
        if (!reader.TryReadTo(out line, (byte)'\n'))
            return false;

        if (!line.IsEmpty && line.Slice(line.Length - 1, 1).FirstSpan[0] == '\r')
            line = line.Slice(0, line.Length - 1);

        return true;
    }

    private static HttpContext BuildHttpContext(
        IServiceProvider services,
        string method,
        string path,
        string query,
        string protocol,
        IHeaderDictionary headers,
        byte[] body)
    {
        FeatureCollection features = [];

        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Method = method,
            Path = path,
            QueryString = query,
            Protocol = protocol,
            Headers = headers,
            Body = new MemoryStream(body)
        });

        DefaultHttpContext context = new DefaultHttpContext(features);
        context.RequestServices = services;
        return context;
    }

    public void Dispose()
    {
        _bufferLength = 0;
        if (_buffer.Length > InitialBufferSize)
            _buffer = new byte[InitialBufferSize];
    }

    private void Append(byte[] chunk)
    {
        EnsureCapacity(_bufferLength + chunk.Length);
        chunk.CopyTo(_buffer.AsSpan(_bufferLength));
        _bufferLength += chunk.Length;
    }

    private void Compact(int consumedBytes)
    {
        if (consumedBytes <= 0)
            return;

        int remaining = _bufferLength - consumedBytes;
        if (remaining > 0)
            _buffer.AsSpan(consumedBytes, remaining).CopyTo(_buffer);

        _bufferLength = remaining;
    }

    private void EnsureCapacity(int neededCapacity)
    {
        if (_buffer.Length >= neededCapacity)
            return;

        int newLength = Math.Max(_buffer.Length * 2, neededCapacity);
        Array.Resize(ref _buffer, newLength);
    }
}
