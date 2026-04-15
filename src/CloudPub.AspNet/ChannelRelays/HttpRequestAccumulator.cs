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
using System.Text;

namespace CloudPub.ChannelRelays;

internal sealed class HttpRequestAccumulator(IServiceProvider services) : IDisposable
{
    private static readonly byte[] HeaderDelimiter = "\r\n\r\n"u8.ToArray();
    private readonly List<byte> _buffer = [];

    public IReadOnlyList<HttpContext> Accumulate(byte[] chunk)
    {
        _buffer.AddRange(chunk);
        var contexts = new List<HttpContext>();

        while (true)
        {
            int headerEndIndex = FindHeaderEnd();
            if (headerEndIndex == -1)
                break;

            string headerSection = Encoding.ASCII.GetString([.. _buffer], 0, headerEndIndex);
            string[] headerLines = headerSection.Split(["\r\n"], StringSplitOptions.None);

            if (headerLines.Length == 0)
                throw new InvalidDataException("Empty request");

            string[] requestLine = headerLines[0].Split(' ');
            if (requestLine.Length != 3)
                throw new InvalidDataException("Invalid HTTP request line");

            string method = requestLine[0];
            string pathAndQuery = requestLine[1];
            string protocol = requestLine[2];

            (string path, string query) = ParsePath(pathAndQuery);

            HeaderDictionary headers = [];
            int contentLength = 0;
            bool hasChunkedTransferEncoding = false;

            for (int i = 1; i < headerLines.Length; i++)
            {
                if (string.IsNullOrEmpty(headerLines[i]))
                    continue;

                int colonIndex = headerLines[i].IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = headerLines[i].Substring(0, colonIndex).Trim();
                    string value = headerLines[i].Substring(colonIndex + 1).Trim();
                    headers.Append(key, new StringValues(value));

                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        _ = int.TryParse(value, out contentLength);

                    if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                        hasChunkedTransferEncoding = true;
                }
            }

            int requestHeaderLength = headerEndIndex + HeaderDelimiter.Length;
            byte[] bodyBytes;
            int totalRequestLength;

            if (hasChunkedTransferEncoding)
            {
                if (!TryDecodeChunkedBody([.. _buffer], requestHeaderLength, out bodyBytes, out int chunkBytesLength))
                    break;

                totalRequestLength = requestHeaderLength + chunkBytesLength;
                headers.Remove("Transfer-Encoding");
                headers.ContentLength = bodyBytes.Length;
            }
            else
            {
                totalRequestLength = requestHeaderLength + contentLength;
                if (_buffer.Count < totalRequestLength)
                    break;

                bodyBytes = _buffer.Skip(requestHeaderLength).Take(contentLength).ToArray();
            }

            HttpContext context = BuildHttpContext(method, path, query, protocol, headers, bodyBytes);
            
            contexts.Add(context);
            _buffer.RemoveRange(0, totalRequestLength);
        }

        return contexts;
    }

    private int FindHeaderEnd()
    {
        for (int i = 0; i <= _buffer.Count - HeaderDelimiter.Length; i++)
        {
            if (_buffer[i] == HeaderDelimiter[0]
                && _buffer[i + 1] == HeaderDelimiter[1]
                && _buffer[i + 2] == HeaderDelimiter[2]
                && _buffer[i + 3] == HeaderDelimiter[3])
            {
                return i;
            }
        }

        return -1;
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

    private static bool TryDecodeChunkedBody(
        IReadOnlyList<byte> buffer,
        int bodyStartIndex,
        out byte[] decodedBody,
        out int consumedBytesLength)
    {
        decodedBody = [];
        consumedBytesLength = 0;
        List<byte> body = [];
        int cursor = bodyStartIndex;

        while (true)
        {
            int lineEnd = FindCrlf(buffer, cursor);
            if (lineEnd < 0)
                return false;

            string sizeLine = Encoding.ASCII.GetString(buffer.Skip(cursor).Take(lineEnd - cursor).ToArray());
            string chunkSizeToken = sizeLine.Split(';', 2)[0];
            if (!int.TryParse(chunkSizeToken, System.Globalization.NumberStyles.HexNumber, null, out int chunkSize))
                throw new InvalidDataException("Invalid chunk size");

            cursor = lineEnd + 2;

            if (chunkSize == 0)
            {
                int trailersEnd = FindHeaderEnd(buffer, cursor);
                if (trailersEnd < 0)
                    return false;

                cursor = trailersEnd + HeaderDelimiter.Length;
                consumedBytesLength = cursor - bodyStartIndex;
                decodedBody = [.. body];
                return true;
            }

            if (cursor + chunkSize + 2 > buffer.Count)
                return false;

            for (int i = 0; i < chunkSize; i++)
                body.Add(buffer[cursor + i]);

            cursor += chunkSize;
            if (buffer[cursor] != '\r' || buffer[cursor + 1] != '\n')
                throw new InvalidDataException("Malformed chunk terminator");

            cursor += 2;
        }
    }

    private static int FindCrlf(IReadOnlyList<byte> buffer, int start)
    {
        for (int i = start; i < buffer.Count - 1; i++)
        {
            if (buffer[i] == '\r' && buffer[i + 1] == '\n')
                return i;
        }

        return -1;
    }

    private static int FindHeaderEnd(IReadOnlyList<byte> buffer, int start)
    {
        for (int i = start; i <= buffer.Count - HeaderDelimiter.Length; i++)
        {
            if (buffer[i] == HeaderDelimiter[0] &&
                buffer[i + 1] == HeaderDelimiter[1] &&
                buffer[i + 2] == HeaderDelimiter[2] &&
                buffer[i + 3] == HeaderDelimiter[3])
            {
                return i;
            }
        }

        return -1;
    }

    private HttpContext BuildHttpContext(
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
        _buffer.Clear();
    }
}
