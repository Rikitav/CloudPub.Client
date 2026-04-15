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

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Diagnostics;
using System.Text;

namespace CloudPub.Services;

internal sealed class PipelineAccessor
{
    public RequestDelegate? Pipeline { get; set; }
}

internal sealed class PipelineCaptureFilter(PipelineAccessor accessor) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);
            accessor.Pipeline = app.Build();
        };
    }
}

internal sealed class HostedCloudPubPipelineProxyService(
    PipelineAccessor pipelineAccessor,
    HttpRelayDispatcher dispatcher,
    ILogger<HostedCloudPubPipelineProxyService> logger) : BackgroundService
{
    private readonly HttpRelayDispatcher _dispatcher = dispatcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CloudPub pipeline proxy service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpContext context = await _dispatcher.Requests.ReadAsync(stoppingToken);

            try
            {
                if (pipelineAccessor.Pipeline is null)
                    throw new InvalidOperationException("Pipeline delegate was not captured by startup filter.");

                await using MemoryStream responseStream = new MemoryStream();
                context.Response.Body = responseStream;
                await pipelineAccessor.Pipeline!(context).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(stoppingToken).ConfigureAwait(false);

                byte[] responseBytes = SerializeHttpResponse(context, responseStream.ToArray());
                await _dispatcher.ResponceAsync(responseBytes, stoppingToken).ConfigureAwait(false);
                logger.LogDebug("CloudPub pipeline request processed: {method} {path} -> {statusCode}, bytes={bytes}",
                    context.Request.Method, context.Request.Path, context.Response.StatusCode, responseBytes.Length);
            }
            catch (Exception exc)
            {
                Debug.WriteLine(exc);
                logger.LogError(exc, "CloudPub pipeline proxy request failed: {method} {path}", context.Request.Method, context.Request.Path);
                await _dispatcher.ResponceAsync(BuildInternalServerError(), stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("CloudPub pipeline proxy service stopped");
    }

    private static byte[] SerializeHttpResponse(HttpContext context, byte[] body)
    {
        bool isHeadRequest = HttpMethods.IsHead(context.Request.Method);
        byte[] payload = isHeadRequest ? [] : body;
        var headers = context.Response.Headers;

        if (!headers.ContainsKey("Content-Length") && !headers.ContainsKey("Transfer-Encoding"))
            headers.ContentLength = payload.Length;

        StringBuilder builder = new StringBuilder();
        builder.Append("HTTP/1.1 ");
        builder.Append(context.Response.StatusCode);
        builder.Append(' ');
        builder.Append(context.Response.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>()?.ReasonPhrase ?? "OK");
        builder.Append("\r\n");

        foreach (KeyValuePair<string, StringValues> header in headers)
        {
            foreach (string? value in header.Value)
            {
                builder.Append(header.Key);
                builder.Append(": ");
                builder.Append(value);
                builder.Append("\r\n");
            }
        }

        builder.Append("\r\n");
        byte[] headersBytes = Encoding.ASCII.GetBytes(builder.ToString());
        byte[] response = new byte[headersBytes.Length + payload.Length];
        Buffer.BlockCopy(headersBytes, 0, response, 0, headersBytes.Length);
        Buffer.BlockCopy(payload, 0, response, headersBytes.Length, payload.Length);
        return response;
    }

    private static byte[] BuildInternalServerError()
    {
        const string message = "Internal Server Error";
        string response = $"HTTP/1.1 500 Internal Server Error\r\nContent-Type: text/plain\r\nContent-Length: {message.Length}\r\n\r\n{message}";
        return Encoding.ASCII.GetBytes(response);
    }
}
